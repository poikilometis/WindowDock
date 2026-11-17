#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;


namespace WindowDock
{
    internal sealed class HookProvider
    {
        public string Id,Name,RelativePath,CommandName,HelpKey;
        public string Help => L10n.Get(HelpKey);
        public string[] Events;
        public static readonly HookProvider[] All={
            new HookProvider { Id="claude",Name="Claude Code",CommandName="claude",RelativePath=@".claude\settings.local.json",
                Events=new[]{"SessionStart","UserPromptSubmit","PreToolUse","PostToolUse","PermissionRequest","Notification","Stop","StopFailure","SessionEnd"},
                HelpKey="AgentIntegrations.WorkingPermissionRequestsCompletionAndErrorsRestart" },
            new HookProvider { Id="codex",Name="Codex",CommandName="codex",RelativePath=@".codex\hooks.json",
                Events=new[]{"SessionStart","UserPromptSubmit","PreToolUse","PostToolUse","PermissionRequest","Stop","Interrupt","SessionEnd"},
                HelpKey="AgentIntegrations.WorkingPermissionRequestsAndCompletionRestartCodex" },
            new HookProvider { Id="gemini",Name="Gemini CLI",CommandName="gemini",RelativePath=@".gemini\settings.json",
                Events=new[]{"SessionStart","BeforeAgent","BeforeTool","AfterTool","Notification","AfterAgent","SessionEnd"},
                HelpKey="AgentIntegrations.WorkingPermissionRequestsAndCompletionRestartThe" }
        };
        public static HookProvider Find(string id) { return All.FirstOrDefault(delegate(HookProvider p){return p.Id==id;}); }
        public override string ToString() { return Name; }
        public string FindCommand()
        {
            foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
                foreach(string suffix in new[]{".exe",".cmd",".bat",".ps1"})
                    try { string path=Path.Combine(directory.Trim().Trim('"'),CommandName+suffix); if(File.Exists(path)) return path; } catch { }
            return null;
        }
        public static HookProvider Detect(ProjectCommand command)
        {
            string executable=Path.GetFileNameWithoutExtension(command.Executable??"");
            HookProvider direct=All.FirstOrDefault(delegate(HookProvider p){return String.Equals(executable,p.CommandName,StringComparison.OrdinalIgnoreCase);});
            if(direct!=null) return direct;
            List<HookProvider> found=All.Where(delegate(HookProvider p){return Regex.IsMatch(command.Arguments??"",@"(^|[\s\""';&|])"+p.CommandName+@"(?:\.exe|\.cmd|\.ps1)?(?=$|[\s\""';&|])",RegexOptions.IgnoreCase);}).ToList();
            return found.Count==1?found[0]:null;
        }
    }
    internal static class HookCommands
    {
        public const int ProtocolVersion=1;
        public const string ManifestName="WindowDock.integration.json";
        public const string Marker="# WindowDock managed hook v1";
        static string PS(string value) { return "'"+(value??"").Replace("'","''")+"'"; }
        public static string Script(HookProvider provider,string ev,string executable)
        {
            return Marker+"; provider="+provider.Id+"; event="+ev+"\n"+
                "$ErrorActionPreference='Stop'\ntry {\n"+
                "$reader=[IO.StreamReader]::new([Console]::OpenStandardInput(),[Text.UTF8Encoding]::new($false),$true); $payload=$reader.ReadToEnd(); $reader.Dispose()\n"+
                "$manifest=Get-Content -LiteralPath "+PS(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable)),ManifestName))+" -Raw | ConvertFrom-Json\n"+
                "if($manifest.product -ne 'WindowDock' -or $manifest.hookProtocol -ne "+ProtocolVersion+"){[Console]::Out.Write('{}');exit 0}\n"+
                "$p=New-Object System.Diagnostics.Process\n"+
                "$p.StartInfo.FileName="+PS(executable)+"\n"+
                "$ticks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks\n"+
                "$p.StartInfo.Arguments="+PS("--agent-hook "+provider.Id+" --event "+ev+" --event-ticks ")+"+$ticks\n"+
                "$p.StartInfo.UseShellExecute=$false\n$p.StartInfo.CreateNoWindow=$true\n"+
                "$p.StartInfo.RedirectStandardInput=$true\n$p.StartInfo.RedirectStandardOutput=$true\n$p.StartInfo.RedirectStandardError=$true\n"+
                "try { [void]$p.Start(); $bytes=[Text.Encoding]::UTF8.GetBytes($payload); $stream=$p.StandardInput.BaseStream; $stream.Write($bytes,0,$bytes.Length); $stream.Flush(); $stream.Close(); if(-not $p.WaitForExit(2200)){try{$p.Kill()}catch{}} } finally {$p.Dispose()}\n"+
                "} catch {}\n[Console]::Out.Write('{}')\nexit 0\n";
        }
        public static string Create(HookProvider provider,string ev,string executable)
        {
            return "powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(provider,ev,executable)));
        }
        public static bool IsOwned(string command,string provider)
        {
            if(String.IsNullOrEmpty(command)) return false;
            Match match=Regex.Match(command,@"^powershell\.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand ([A-Za-z0-9+/=]+)$",RegexOptions.IgnoreCase);
            if(!match.Success) return false;
            try
            {
                string script=Encoding.Unicode.GetString(Convert.FromBase64String(match.Groups[1].Value));
                return script.StartsWith(Marker+"; provider="+provider+"; event=",StringComparison.Ordinal);
            }
            catch(FormatException) { return false; }
        }
        public static bool IsOwned(JsonNode handler,string provider)
        {
            if(handler.Kind!="object"||handler.String("type")!="command") return false;
            bool owned=false;
            foreach(string key in new[]{"command","commandWindows","command_windows"})
            {
                JsonNode node=handler.Get(key); if(node==null) continue;
                if(node.Kind!="string"||!IsOwned(node.Text,provider)) return false; owned=true;
            }
            return owned;
        }
        public static string Group(HookProvider provider,string ev,string executable)
        {
            string command=JsonSyntax.Quote(Create(provider,ev,executable));
            string handler="{\"type\":\"command\",\"command\":"+command+",\"timeout\":"+(provider.Id=="gemini"?"5000":ev=="SessionEnd"||ev=="Interrupt"?"3":"5");
            if(provider.Id=="codex") handler+=",\"commandWindows\":"+command;
            if(provider.Id!="gemini"&&ev!="SessionEnd"&&ev!="Interrupt") handler+=",\"async\":true";
            if(provider.Id=="gemini") handler+=",\"name\":"+JsonSyntax.Quote("windowdock-"+ev.ToLowerInvariant());
            handler+="}";
            string matcher=provider.Id=="claude"&&ev=="Notification"?"\"matcher\":\"permission_prompt|elicitation_dialog\",":"";
            return "{"+matcher+"\"hooks\":["+handler+"]}";
        }
    }
    internal sealed class HookConfigEditor
    {
        public static string Edit(string source,HookProvider provider,string executable,bool install)
        {
            JsonNode root=JsonSyntax.Parse(source); if(root.Kind!="object") throw new FormatException(L10n.Get("AgentIntegrations.TheSettingsRootMustBeAJSON"));
            JsonNode hooks=root.Get("hooks");
            if(hooks!=null&&hooks.Kind!="object")
            { if(!install) return source; throw new FormatException(L10n.Get("AgentIntegrations.TheHooksFieldMustBeAnObject")); }
            List<string> properties=new List<string>(); HashSet<string> seen=new HashSet<string>();
            if(hooks!=null)
                foreach(JsonProperty property in hooks.Properties)
                {
                    string value=property.Value.Raw(source); bool target=provider.Events.Contains(property.Name);
                    if(property.Value.Kind=="array")
                    {
                        bool changed=false; List<string> groups=new List<string>();
                        foreach(JsonNode group in property.Value.Items)
                        {
                            JsonNode handlers=group.Get("hooks");
                            if(group.Kind!="object"||handlers==null||handlers.Kind!="array") { groups.Add(group.Raw(source)); continue; }
                            List<JsonNode> kept=handlers.Items.Where(delegate(JsonNode item){return !HookCommands.IsOwned(item,provider.Id);}).ToList();
                            if(kept.Count==handlers.Items.Count) { groups.Add(group.Raw(source)); continue; }
                            changed=true;
                            if(kept.Count>0)
                            {
                                string replacement="["+String.Join(",",kept.Select(delegate(JsonNode n){return n.Raw(source);}).ToArray())+"]";
                                string raw=group.Raw(source); int relative=handlers.Start-group.Start;
                                groups.Add(raw.Substring(0,relative)+replacement+raw.Substring(handlers.End-group.Start));
                            }
                        }
                        if(install&&target) { groups.Add(HookCommands.Group(provider,property.Name,executable)); changed=true; }
                        if(changed) value="[\n      "+String.Join(",\n      ",groups.ToArray())+"\n    ]";
                    }
                    else if(target) throw new FormatException(L10n.Format("AgentIntegrations.EventMustContainAHooksArrayThe", property.Name));
                    properties.Add(source.Substring(property.Start,property.Value.Start-property.Start)+value); seen.Add(property.Name);
                }
            if(install)
                foreach(string ev in provider.Events)
                    if(!seen.Contains(ev)) properties.Add(JsonSyntax.Quote(ev)+": [\n      "+HookCommands.Group(provider,ev,executable)+"\n    ]");
            if(!install&&hooks==null) return source;
            string updated="{\n    "+String.Join(",\n    ",properties.ToArray())+"\n  }";
            if(hooks!=null&&updated==hooks.Raw(source)) return source;
            return JsonSyntax.SetProperty(source,root,"hooks",updated);
        }
        public static int OwnedCount(string source,HookProvider provider)
        {
            JsonNode root=JsonSyntax.Parse(source),hooks=root.Get("hooks"); if(hooks==null||hooks.Kind!="object") return 0;
            return hooks.Properties.Where(delegate(JsonProperty p){return p.Value.Kind=="array";}).SelectMany(delegate(JsonProperty p){return p.Value.Items;})
                .Where(delegate(JsonNode g){return g.Kind=="object"&&g.Get("hooks")!=null&&g.Get("hooks").Kind=="array";})
                .SelectMany(delegate(JsonNode g){return g.Get("hooks").Items;}).Count(delegate(JsonNode h){return HookCommands.IsOwned(h,provider.Id);});
        }
    }
    [DataContract] public sealed class HookInstallRecord
    {
        [DataMember] public string Provider,Project,ConfigPath,BackupPath,OriginalHash,InstalledHash,Executable;
        [DataMember] public bool Existed;
        [DataMember] public int Version;
    }
    internal sealed class HookFile
    {
        public string PathName,Text;
        public byte[] Bytes;
        public Encoding Encoding;
        public bool Preamble,Existed;
        public static HookFile Read(string path)
        {
            HookFile file=new HookFile { PathName=path,Existed=File.Exists(path),Encoding=new UTF8Encoding(false),Text="{}\n",Bytes=new byte[0] };
            if(!file.Existed) return file;
            FileInfo info=new FileInfo(path); if(info.Length>8*1024*1024) throw new IOException(L10n.Format("AgentIntegrations.TheSettingsFileIsTooLarge", path));
            file.Bytes=File.ReadAllBytes(path);
            using(StreamReader reader=new StreamReader(new MemoryStream(file.Bytes),Encoding.UTF8,true)) { file.Text=reader.ReadToEnd(); file.Encoding=reader.CurrentEncoding; }
            byte[] prefix=file.Encoding.GetPreamble(); file.Preamble=prefix.Length>0&&file.Bytes.Take(prefix.Length).SequenceEqual(prefix);
            return file;
        }
        public byte[] Encode(string text)
        {
            byte[] body=Encoding.GetBytes(text);
            return Preamble?Encoding.GetPreamble().Concat(body).ToArray():body;
        }
    }
    internal sealed class HookChange
    {
        public HookProvider Provider;
        public HookFile Before;
        public byte[] After;
        public string RecordPath;
        public HookInstallRecord Record;
        public bool DeleteConfig;
        public bool HasChanges { get { return !Before.Bytes.SequenceEqual(After); } }
    }
    internal static class HookInstaller
    {
        public static string NormalizeProject(string path)
        {
            if(String.IsNullOrWhiteSpace(path)) throw new IOException(L10n.Get("AgentIntegrations.SelectAProjectFolder"));
            string root=System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())).TrimEnd('\\','/');
            if(root.Length==2&&root[1]==':') root+="\\";
            if(!Directory.Exists(root)) throw new DirectoryNotFoundException(L10n.Format("AgentIntegrations.ProjectFolderNotFound", root));
            return root;
        }
        public static string ManagedPath(string root,string relative)
        {
            string normalized=NormalizeProject(root),path=System.IO.Path.GetFullPath(System.IO.Path.Combine(normalized,relative));
            if(!path.StartsWith(normalized.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)) throw new IOException(L10n.Get("AgentIntegrations.TheSettingsPathIsOutsideTheProject"));
            string current=path;
            while(!String.Equals(current,normalized,StringComparison.OrdinalIgnoreCase))
            {
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException(L10n.Format("AgentIntegrations.TheSettingsPathContainsASymbolicLink", current));
                current=System.IO.Path.GetDirectoryName(current); if(current==null) throw new IOException(L10n.Get("AgentIntegrations.InvalidProjectPath"));
            }
            return path;
        }
        public static string Hash(byte[] bytes)
        {
            using(SHA256 algorithm=SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-","");
        }
        public static HookChange Plan(string project,HookProvider provider,string executable,bool install)
        {
            string root=NormalizeProject(project),config=ManagedPath(root,provider.RelativePath),recordPath=ManagedPath(root,@".windowdock\integrations\"+provider.Id+".json");
            HookFile file=HookFile.Read(config); string updated=HookConfigEditor.Edit(file.Text,provider,executable,install);
            HookInstallRecord record=File.Exists(recordPath)?Store.Read<HookInstallRecord>(recordPath):null;
            if(record!=null&&(record.Version!=1||record.Provider!=provider.Id||!String.Equals(record.Project,root,StringComparison.OrdinalIgnoreCase)||!String.Equals(record.ConfigPath,config,StringComparison.OrdinalIgnoreCase)))
                throw new IOException(L10n.Get("AgentIntegrations.TheIntegrationJournalDoesNotMatchThe"));
            return new HookChange { Provider=provider,Before=file,After=file.Encode(updated),RecordPath=recordPath,Record=record };
        }
        public static int Install(string project,IEnumerable<HookProvider> providers,string executable)
        {
            string root=NormalizeProject(project); using(FileStream gate=AcquireGate(root)) return InstallCore(root,providers,executable);
        }
        static FileStream AcquireGate(string root)
        {
            EnsurePrivateState(root); string path=ManagedPath(root,@".windowdock\integrations\setup.lock"); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            try { return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException) { throw new IOException(L10n.Get("AgentIntegrations.SetupIsAlreadyRunningForThisProject")); }
        }
        static void CheckUnchanged(HookChange change)
        {
            bool exists=File.Exists(change.Before.PathName);
            if(exists!=change.Before.Existed||(exists&&Hash(File.ReadAllBytes(change.Before.PathName))!=Hash(change.Before.Bytes)))
                throw new IOException(L10n.Get("AgentIntegrations.TheSettingsChangedDuringPreparationTryAgain"));
        }
        static int InstallCore(string project,IEnumerable<HookProvider> providers,string executable)
        {
            string root=NormalizeProject(project);
            List<HookChange> changes=providers.Distinct().Select(delegate(HookProvider p){return Plan(root,p,executable,true);}).ToList();
            if(changes.Count==0) throw new IOException(L10n.Get("AgentIntegrations.SelectAtLeastOneAgent"));
            foreach(HookChange change in changes) CheckUnchanged(change);
            EnsurePrivateState(root);
            List<HookChange> written=new List<HookChange>(); int count=0;
            try
            {
                foreach(HookChange change in changes)
                {
                    if(!change.HasChanges&&change.Record!=null&&change.Record.InstalledHash==Hash(change.After)) continue;
                    CheckUnchanged(change);
                    HookInstallRecord record=change.Record==null?null:GroupPersistence.Clone(change.Record);
                    if(record==null||record.InstalledHash!=Hash(change.Before.Bytes))
                    {
                        // A user edit since setup becomes the new uninstall baseline.
                        bool ownedBefore=HookConfigEditor.OwnedCount(change.Before.Text,change.Provider)>0;
                        byte[] baseline=record==null&&!ownedBefore?change.Before.Bytes:change.Before.Encode(HookConfigEditor.Edit(change.Before.Text,change.Provider,executable,false));
                        string backup=ManagedPath(root,@".windowdock\backups\"+change.Provider.Id+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".bak");
                        if(change.Before.Existed) AtomicWrite(backup,baseline);
                        record=new HookInstallRecord { Version=1,Provider=change.Provider.Id,Project=root,ConfigPath=change.Before.PathName,
                            BackupPath=change.Before.Existed?backup:null,Existed=change.Before.Existed,OriginalHash=Hash(baseline) };
                    }
                    EnsurePrivateState(root);
                    AtomicWrite(change.Before.PathName,change.After); written.Add(change);
                    record.InstalledHash=Hash(change.After); record.Executable=executable;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(change.RecordPath)); Store.Write(change.RecordPath,record); count++;
                }
            }
            catch
            {
                foreach(HookChange change in written.AsEnumerable().Reverse())
                    try
                    {
                        if(File.Exists(change.Before.PathName)&&Hash(File.ReadAllBytes(change.Before.PathName))==Hash(change.After))
                        { if(change.Before.Existed) AtomicWrite(change.Before.PathName,change.Before.Bytes); else File.Delete(change.Before.PathName); }
                        if(change.Record!=null) Store.Write(change.RecordPath,change.Record); else if(File.Exists(change.RecordPath)) File.Delete(change.RecordPath);
                    }
                    catch(Exception ex) { Store.Log("Integration rollback: "+ex.Message); }
                throw;
            }
            return count;
        }
        public static int Uninstall(string project,IEnumerable<HookProvider> providers,string executable)
        {
            string root=NormalizeProject(project);
            using(FileStream gate=AcquireGate(root))
            {
                List<HookChange> changes=providers.Distinct().Select(delegate(HookProvider p){return Plan(root,p,executable,false);}).ToList();
                foreach(HookChange change in changes)
                {
                    HookInstallRecord record=change.Record;
                    if(record!=null&&change.Before.Existed&&Hash(change.Before.Bytes)==record.InstalledHash)
                    {
                        if(record.Existed)
                        {
                            string backup=ManagedPath(record.Project,BackupRelative(record.Project,record.BackupPath));
                            byte[] original=File.ReadAllBytes(backup); if(Hash(original)!=record.OriginalHash) throw new IOException(L10n.Get("AgentIntegrations.TheBackupWasModifiedTheSettingsWere"));
                            change.After=original;
                        }
                        else { change.DeleteConfig=true; change.After=new byte[0]; }
                    }
                    else if(!change.Before.Existed||HookConfigEditor.OwnedCount(change.Before.Text,change.Provider)==0) change.After=change.Before.Bytes;
                }
                List<HookChange> written=new List<HookChange>(); int count=0;
                try
                {
                    foreach(HookChange change in changes)
                    {
                        CheckUnchanged(change);
                        if(change.HasChanges||change.DeleteConfig)
                        {
                            if(change.DeleteConfig) File.Delete(change.Before.PathName); else AtomicWrite(change.Before.PathName,change.After);
                            count++;
                        }
                        written.Add(change); if(File.Exists(change.RecordPath)) File.Delete(change.RecordPath);
                    }
                }
                catch
                {
                    foreach(HookChange change in written.AsEnumerable().Reverse())
                        try { if(change.Before.Existed) AtomicWrite(change.Before.PathName,change.Before.Bytes); if(change.Record!=null) Store.Write(change.RecordPath,change.Record); }
                        catch(Exception ex) { Store.Log("Integration rollback: "+ex.Message); }
                    throw;
                }
                return count;
            }
        }
                static string BackupRelative(string root,string path)
        {
            string prefix=root.TrimEnd('\\')+"\\";
            if(String.IsNullOrEmpty(path)||!System.IO.Path.GetFullPath(path).StartsWith(prefix+@".windowdock\backups\",StringComparison.OrdinalIgnoreCase))
                throw new IOException(L10n.Get("AgentIntegrations.TheBackupPathDoesNotBelongTo"));
            return System.IO.Path.GetFullPath(path).Substring(prefix.Length);
        }
        static void EnsurePrivateState(string root)
        {
            string path=ManagedPath(root,@".windowdock\.gitignore"); string text=File.Exists(path)?File.ReadAllText(path):"";
            if(!text.Contains("# WindowDock private state")) AtomicWrite(path,Encoding.UTF8.GetBytes(text+(text.EndsWith("\n",StringComparison.Ordinal)?"":"\n")+"# WindowDock private state\nbackups/\nintegrations/\n"));
        }
        public static void AtomicWrite(string path,byte[] bytes)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); string temporary=path+".windowdock-"+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(FileStream stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                if(File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
            }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
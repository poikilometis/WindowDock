#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;


namespace WindowDock
{
    [DataContract] public sealed class ProjectProfile
    {
        [DataMember] public string Id,Name,WorkspaceId;
        [DataMember] public List<ProjectCommand> Commands;
        public static ProjectProfile Create(string workspace)
        {
            return new ProjectProfile { Id=Guid.NewGuid().ToString("N"),Name=L10n.Get("ProjectModels.NewProject"),WorkspaceId=workspace,Commands=new List<ProjectCommand>() };
        }
        public void Validate()
        {
            if(String.IsNullOrEmpty(Id)) Id=Guid.NewGuid().ToString("N");
            if(String.IsNullOrWhiteSpace(Name)) Name=L10n.Get("ProjectModels.Project");
            if(Commands==null) Commands=new List<ProjectCommand>();
            Commands.RemoveAll(delegate(ProjectCommand c){return c==null;});
            if(Commands.Count>64) Commands=Commands.Take(64).ToList();
            foreach(ProjectCommand command in Commands) command.Validate();
        }
    }
    [DataContract] public sealed class ProjectCommand
    {
        [DataMember] public string Id,Name,Executable,Arguments,Directory,TitlePattern,IntegrationId;
        [DataMember] public List<EnvironmentEntry> Environment;
        public static ProjectCommand Create()
        {
            return new ProjectCommand { Id=Guid.NewGuid().ToString("N"),Name=L10n.Get("ProjectModels.Terminal"),Executable=OperatingSystem.IsWindows()?"powershell.exe":System.Environment.GetEnvironmentVariable("SHELL")??"/bin/bash",Arguments=OperatingSystem.IsWindows()?"-NoExit":"",
                Directory=System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),TitlePattern="",Environment=new List<EnvironmentEntry>() };
        }
        public void Validate()
        {
            if(String.IsNullOrEmpty(Id)) Id=Guid.NewGuid().ToString("N");
            Name=String.IsNullOrWhiteSpace(Name)?L10n.Get("ProjectModels.Terminal"):Name.Trim();
            if(IntegrationId!="auto"&&HookProvider.Find(IntegrationId)==null) IntegrationId="";
            Executable=(Executable??"").Trim(); Arguments=Arguments??""; Directory=Directory??""; TitlePattern=TitlePattern??"";
            if(Environment==null) Environment=new List<EnvironmentEntry>();
            Environment.RemoveAll(delegate(EnvironmentEntry e){return e==null||String.IsNullOrWhiteSpace(e.Key)||e.Key.IndexOf('=')>=0||e.Key.IndexOf('\0')>=0|| (e.Value??"").IndexOf('\0')>=0;});
        }
    }
    [DataContract] public sealed class EnvironmentEntry
    {
        [DataMember] public string Key,Value;
    }
}

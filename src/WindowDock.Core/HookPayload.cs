#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using System.Linq;
namespace WindowDock {
    [DataContract] public sealed class AgentSignal
    {
        [DataMember] public long Handle;
        [DataMember] public string AgentId,Status,Message;
        [DataMember] public string Provider,SessionKey,Directory;
        [DataMember(EmitDefaultValue = false)] public string ConversationId;
        [DataMember] public long ConsoleHandle,EventUtcTicks;
        [DataMember] public System.Collections.Generic.List<HookProcess> ProcessChain;
    }
    [DataContract] public sealed class HookProcess
    {
        [DataMember] public int Id;
        [DataMember] public long StartTicks;
    }
internal static class HookPayload {
        public static string SessionKey(string provider,string session)
        { return provider+":"+HookInstaller.Hash(Encoding.UTF8.GetBytes(session)); }
        public static string Status(HookProvider provider,string ev,JsonNode payload)
        {
            if(ev=="SessionStart"||ev=="SessionEnd"||ev=="Interrupt") return "clear";
            if(ev=="Stop"||ev=="AfterAgent") return "done";
            if(ev=="StopFailure") return "error";
            if(ev=="PermissionRequest") return "waiting";
            if(ev=="Notification")
            {
                string type=payload.String("notification_type");
                if(provider.Id=="claude") return type=="permission_prompt"||type=="elicitation_dialog"?"waiting":null;
                if(provider.Id=="gemini") return type=="ToolPermission"?"waiting":null;
                return null;
            }
            if(ev=="UserPromptSubmit"||ev=="BeforeAgent"||ev=="PreToolUse"||ev=="PostToolUse"||ev=="BeforeTool"||ev=="AfterTool") return "working";
            return null;
        }
        public static AgentSignal Parse(HookProvider provider,string fallback,string json,string agentId,List<HookProcess> parents,long console,long eventTicks)
        {
            JsonNode root=JsonSyntax.Parse(json); if(root.Kind!="object") return null;
            string ev=root.String("hook_event_name")??fallback;
            if(!provider.Events.Contains(ev)) return null;
            // Explicit subagent events must never mark a whole terminal as complete.
            if(root.IsTrue("is_subagent")) return null;
            string status=Status(provider,ev,root); if(status==null) return null;
            string session=root.String("session_id")??root.String("thread_id");
            if(String.IsNullOrWhiteSpace(session)||session.Length>512) return null;
            string directory=root.String("cwd")??""; if(directory.Length>2048) directory=directory.Substring(0,2048);
            return new AgentSignal { AgentId=agentId,Provider=provider.Id,SessionKey=SessionKey(provider.Id,session),Directory=directory,
                ConversationId=AgentResume.ParseId(session),
                ConsoleHandle=console,ProcessChain=parents,EventUtcTicks=eventTicks,Status=status,
                Message=status=="waiting"?L10n.Get("HookPayload.TheAgentIsRequestingPermission"):status=="error"?L10n.Get("HookPayload.AgentError"):"" };
        }
}
}

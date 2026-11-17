#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace WindowDock
{
    [DataContract] public sealed class WindowRule
    {
        [DataMember] public string Id, Name, ProcessPattern, TitlePattern, WorkspaceId;
        [DataMember] public bool Enabled, Exclude, TerminalOnly, Overflow;
        public static WindowRule Create(string workspace)
        {
            return new WindowRule { Id = Guid.NewGuid().ToString("N"), Name = L10n.Get("WindowRules.NewRule"), Enabled = true,
                Overflow = true, WorkspaceId = workspace, ProcessPattern = "", TitlePattern = "" };
        }
        public void Validate()
        {
            if (String.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (String.IsNullOrWhiteSpace(Name)) Name = L10n.Get("WindowRules.Rule");
            Name = Name.Trim(); if (Name.Length > 80) Name = Name.Substring(0, 80);
            ProcessPattern = (ProcessPattern ?? "").Trim(); TitlePattern = (TitlePattern ?? "").Trim();
            if (ProcessPattern.Length > 256) ProcessPattern = ProcessPattern.Substring(0, 256);
            if (TitlePattern.Length > 512) TitlePattern = TitlePattern.Substring(0, 512);
        }
        internal bool Matches(WindowCandidate candidate)
        {
            if (!Enabled || candidate == null || (TerminalOnly && !candidate.IsTerminal)) return false;
            string process = ProcessPattern ?? "";
            if (process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process = process.Substring(0, process.Length - 4);
            return Wildcard.Matches(process, candidate.ProcessName) && Wildcard.Matches(TitlePattern, candidate.Title);
        }
    }
    internal static class Wildcard
    {
        // Iterative glob matching: user patterns are not executable regular expressions.
        public static bool Matches(string pattern, string value)
        {
            if (String.IsNullOrWhiteSpace(pattern)) return true;
            value = value ?? ""; pattern = pattern.Trim();
            if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0)
                return value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            int p = 0, v = 0, star = -1, retry = 0;
            while (v < value.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || Char.ToUpperInvariant(pattern[p]) == Char.ToUpperInvariant(value[v]))) { p++; v++; }
                else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = v; }
                else if (star >= 0) { p = star + 1; v = ++retry; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
    }
    internal sealed class RuleDecision
    {
        public bool Excluded;
        public WindowRule Rule;
    }
    internal static class RuleEngine
    {
        public static RuleDecision Evaluate(IEnumerable<WindowRule> rules, WindowCandidate candidate)
        {
            List<WindowRule> matches = rules.Where(delegate(WindowRule r) { return r.Matches(candidate); }).ToList();
            WindowRule exclusion = matches.FirstOrDefault(delegate(WindowRule r) { return r.Exclude; });
            return new RuleDecision { Excluded = exclusion != null, Rule = exclusion ?? matches.FirstOrDefault() };
        }
    }
}
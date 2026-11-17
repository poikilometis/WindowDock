#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WindowDock
{
    // Offsets preserve unrelated settings, comments and numeric values verbatim.
    internal sealed class JsonNode
    {
        public string Kind,Text;
        public int Start,End;
        public readonly List<JsonProperty> Properties=new List<JsonProperty>();
        public readonly List<JsonNode> Items=new List<JsonNode>();
        public JsonNode Get(string key) { JsonProperty p=Properties.FirstOrDefault(delegate(JsonProperty value){return value.Name==key;}); return p==null?null:p.Value; }
        public string String(string key) { JsonNode value=Get(key); return value!=null&&value.Kind=="string"?value.Text:null; }
        public bool IsTrue(string key) { JsonNode value=Get(key); return value!=null&&value.Kind=="true"; }
        public string Raw(string source) { return source.Substring(Start,End-Start); }
    }
    internal sealed class JsonProperty
    {
        public string Name;
        public int Start;
        public JsonNode Value;
    }
    internal sealed class JsonSyntax
    {
        readonly string source;
        int index;
        JsonSyntax(string text) { source=text; }
        public static JsonNode Parse(string text)
        {
            if(text==null||text.Length>8*1024*1024) throw new FormatException(L10n.Get("JsonSyntax.TheJSONFileIsTooLarge"));
            JsonSyntax parser=new JsonSyntax(text); parser.Trivia(); JsonNode root=parser.Value(0); parser.Trivia();
            if(parser.index!=text.Length) throw new FormatException(L10n.Get("JsonSyntax.UnexpectedDataAfterJSON")); return root;
        }
        void Trivia()
        {
            while(index<source.Length)
            {
                if(Char.IsWhiteSpace(source[index])||source[index]=='\uFEFF') { index++; continue; }
                if(source[index]=='/'&&index+1<source.Length&&source[index+1]=='/')
                { index+=2; while(index<source.Length&&source[index]!='\n') index++; continue; }
                if(source[index]=='/'&&index+1<source.Length&&source[index+1]=='*')
                {
                    int end=source.IndexOf("*/",index+2,StringComparison.Ordinal); if(end<0) throw new FormatException(L10n.Get("JsonSyntax.UnterminatedJSONComment")); index=end+2; continue;
                }
                break;
            }
        }
        JsonNode Value(int depth)
        {
            if(depth>100||index>=source.Length) throw new FormatException(L10n.Get("JsonSyntax.InvalidOrExcessivelyNestedJSON"));
            JsonNode node=new JsonNode { Start=index };
            char token=source[index];
            if(token=='{'||token=='[')
            {
                bool obj=token=='{'; node.Kind=obj?"object":"array"; index++; Trivia();
                char close=obj?'}':']'; HashSet<string> names=new HashSet<string>(StringComparer.Ordinal);
                while(index<source.Length&&source[index]!=close)
                {
                    if(obj)
                    {
                        int start=index; string name=ReadString(); if(!names.Add(name)) throw new FormatException(L10n.Format("JsonSyntax.DuplicateJSONKey", name));
                        Trivia(); Require(':'); Trivia(); JsonNode child=Value(depth+1); node.Properties.Add(new JsonProperty { Name=name,Start=start,Value=child });
                    }
                    else node.Items.Add(Value(depth+1));
                    Trivia(); if(index<source.Length&&source[index]==close) break;
                    Require(','); Trivia(); // Existing JSONC files may have a trailing comma.
                }
                Require(close);
            }
            else if(token=='"') { node.Kind="string"; node.Text=ReadString(); }
            else
            {
                int start=index; while(index<source.Length&&!Char.IsWhiteSpace(source[index])&&",]} /".IndexOf(source[index])<0) index++;
                string raw=source.Substring(start,index-start);
                if(raw=="true"||raw=="false"||raw=="null") node.Kind=raw;
                else
                {
                    if(!System.Text.RegularExpressions.Regex.IsMatch(raw,@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$")) throw new FormatException(L10n.Get("JsonSyntax.InvalidJSONValue"));
                    node.Kind="number"; node.Text=raw;
                }
            }
            node.End=index; return node;
        }
        string ReadString()
        {
            Require('"'); StringBuilder value=new StringBuilder();
            while(index<source.Length)
            {
                char next=source[index++]; if(next=='"') return value.ToString();
                if(next<' ') throw new FormatException(L10n.Get("JsonSyntax.ControlCharacterInAJSONString"));
                if(next!='\\') { value.Append(next); continue; }
                if(index>=source.Length) break;
                next=source[index++];
                if(next=='"'||next=='\\'||next=='/') value.Append(next);
                else if(next=='b') value.Append('\b'); else if(next=='f') value.Append('\f'); else if(next=='n') value.Append('\n');
                else if(next=='r') value.Append('\r'); else if(next=='t') value.Append('\t');
                else if(next=='u')
                {
                    int code; if(index+4>source.Length||!Int32.TryParse(source.Substring(index,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out code)) throw new FormatException(L10n.Get("JsonSyntax.InvalidUnicodeEscapeInJSON"));
                    index+=4; value.Append((char)code);
                }
                else throw new FormatException(L10n.Get("JsonSyntax.InvalidEscapeSequenceInJSON"));
            }
            throw new FormatException(L10n.Get("JsonSyntax.UnterminatedJSONString"));
        }
        void Require(char character)
        {
            if(index>=source.Length||source[index]!=character) throw new FormatException(L10n.Format("JsonSyntax.ExpectedInJSON", character)); index++;
        }
        public static string Quote(string value)
        {
            StringBuilder result=new StringBuilder("\"");
            foreach(char character in value??"")
            {
                if(character=='"'||character=='\\') result.Append('\\').Append(character);
                else if(character<' ') result.Append("\\u").Append(((int)character).ToString("x4",CultureInfo.InvariantCulture));
                else result.Append(character);
            }
            return result.Append('"').ToString();
        }
        public static string ReplaceValue(string source,JsonNode node,string replacement)
        { return source.Substring(0,node.Start)+replacement+source.Substring(node.End); }
        public static string SetProperty(string source,JsonNode obj,string name,string value)
        {
            JsonNode existing=obj.Get(name); if(existing!=null) return ReplaceValue(source,existing,value);
            int position=obj.Properties.Count>0?obj.Properties[obj.Properties.Count-1].Value.End:obj.End-1;
            string separator=obj.Properties.Count>0?",":"";
            return source.Insert(position,separator+"\n  "+Quote(name)+": "+value+"\n");
        }
    }
}
using System;
using System.Security.Cryptography;
using System.Text;
using LastWatch.Core.Persistence;
using LastWatch.Core.State;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    [Serializable] sealed class M4UnifiedDto { public int schemaVersion=1; public string runStateJson; public string partnerStateJson; }
    public readonly struct M4UnifiedState { public RunState RunState { get; } public PartnerRuntimeState PartnerState { get; }
        public M4UnifiedState(RunState run,PartnerRuntimeState partner){RunState=run;PartnerState=partner;} }
    public static class M4UnifiedRunStateJson
    {
        public static string Serialize(RunState run,PartnerRuntimeState partner,bool pretty=false)
        { if(run==null||partner==null)throw new ArgumentNullException();return JsonUtility.ToJson(new M4UnifiedDto{runStateJson=RunStateJson.Serialize(run),partnerStateJson=PartnerStateJson.Serialize(partner)},pretty); }
        public static M4UnifiedState Deserialize(string json)
        { var d=JsonUtility.FromJson<M4UnifiedDto>(json)??throw new FormatException("Missing M4 unified state.");if(d.schemaVersion!=1)throw new FormatException("Unsupported M4 unified schema.");return new M4UnifiedState(RunStateJson.Deserialize(d.runStateJson),PartnerStateJson.Deserialize(d.partnerStateJson)); }
        public static string CanonicalGameplayHash(RunState run,PartnerRuntimeState partner)
        { string input=RunStateJson.CanonicalHash(run)+"|"+PartnerStateJson.CanonicalHash(partner);using(var sha=SHA256.Create()){var bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(input));var b=new StringBuilder(64);foreach(byte x in bytes)b.Append(x.ToString("x2"));return b.ToString();} }
    }
}

using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace LastWatch.Core.Partner
{
    public static class PartnerStateJson
    {
        public static string Serialize(PartnerRuntimeState state,bool pretty=false)
        { if(state==null)throw new ArgumentNullException(nameof(state)); Validate(state); return JsonUtility.ToJson(state,pretty); }
        public static PartnerRuntimeState Deserialize(string json)
        {
            if(string.IsNullOrWhiteSpace(json))throw new FormatException("Partner state JSON is missing.");
            var state=JsonUtility.FromJson<PartnerRuntimeState>(json)??throw new FormatException("Invalid partner state JSON.");
            if(state.FriendlyIncidents==null)state.FriendlyIncidents=new System.Collections.Generic.List<FriendlyHitIncident>();
            if(state.Data40Facts==null)state.Data40Facts=new System.Collections.Generic.List<Data40Fact>();
            Validate(state); return state;
        }
        public static string CanonicalHash(PartnerRuntimeState state)
        {
            string json=Serialize(state,false); using(var sha=SHA256.Create())
            { var bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(json)); var b=new StringBuilder(64); foreach(byte x in bytes)b.Append(x.ToString("x2")); return b.ToString(); }
        }
        static void Validate(PartnerRuntimeState s)
        {
            M4PartnerMovementPersistence.Validate(s);
            M4PartnerExchangePersistence.Validate(s);
            if(s.ArAmmo<0||s.Parts<0||s.HitPoints<0||s.NextFriendlyIncidentId==0)throw new FormatException("Invalid partner resource/state value.");
            if((s.Life==LastWatch.Core.State.PartnerLifeState.Dead)!=s.DeathTick.HasValue)throw new FormatException("Partner death fact mismatch.");
            if((s.PartnerCombatRngState0|s.PartnerCombatRngState1)==0)throw new FormatException("Partner combat RNG cannot be zero.");
            ulong last=0; foreach(var x in s.FriendlyIncidents){if(x==null||x.IncidentId<=last||x.FireSequenceId==0||x.HitCount<=0)throw new FormatException("Invalid friendly incident ledger.");last=x.IncidentId;}
            ulong cursor=0;foreach(var x in s.Data40Facts){if(x==null||x.Cursor<=cursor||x.EventSequence==0)throw new FormatException("Invalid DATA-40 journal.");cursor=x.Cursor;}
            if(s.Data40AcknowledgedCursor>=s.NextData40Cursor)throw new FormatException("DATA-40 acknowledgement exceeds journal.");
        }
    }
}

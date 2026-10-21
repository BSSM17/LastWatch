using System;
using LastWatch.Core.Identity;
using LastWatch.Core.State;

namespace LastWatch.Core.Persistence
{
    public enum M4StoreDurability { InMemoryContractOnly = 0, PlatformDurable = 1 }
    public enum M4StoreStatus { Committed, GenerationConflict, Failed, Unknown }
    public sealed class M4StoreCandidate
    {
        public RunId RunId { get; }
        public ulong ExpectedGeneration { get; }
        public ulong Generation { get; }
        public M4SessionStage Stage { get; }
        public string Digest { get; }
        public string Json { get; }
        internal M4StoreCandidate(M4SessionDto dto)
        { if(dto.generation==0)throw new ArgumentException("Candidate generation must advance.");RunId=new RunId(dto.runId);Generation=dto.generation;ExpectedGeneration=Generation-1;Stage=dto.stage;Json=M4SessionJson.Encode(dto);Digest=dto.integrityHash; }
    }
    public sealed class M4StoreReceipt
    {
        public RunId RunId { get; }
        public ulong Generation { get; }
        public M4SessionStage Stage { get; }
        public string Digest { get; }
        public M4StoreDurability Durability { get; }
        public M4StoreReceipt(RunId runId,ulong generation,M4SessionStage stage,string digest,M4StoreDurability durability)
        {if(!runId.IsValid||generation==0||string.IsNullOrEmpty(digest)||!Enum.IsDefined(typeof(M4SessionStage),stage)||!Enum.IsDefined(typeof(M4StoreDurability),durability))throw new ArgumentException("Invalid receipt.");RunId=runId;Generation=generation;Stage=stage;Digest=digest;Durability=durability;}
        internal bool Matches(M4StoreCandidate c)=>RunId.Equals(c.RunId)&&Generation==c.Generation&&Stage==c.Stage&&Digest==c.Digest;
    }
    public sealed class M4StoredSnapshot
    {
        public string Json { get; }
        public M4StoreReceipt Receipt { get; }
        public M4StoredSnapshot(string json,M4StoreReceipt receipt){Json=json??throw new ArgumentNullException(nameof(json));Receipt=receipt??throw new ArgumentNullException(nameof(receipt));}
    }
    public sealed class M4StoreResult
    {
        public M4StoreStatus Status { get; }
        public M4StoreReceipt Receipt { get; }
        public string Reason { get; }
        public M4StoreResult(M4StoreStatus status,M4StoreReceipt receipt=null,string reason=null){Status=status;Receipt=receipt;Reason=reason;}
    }
    // Implementations must serialize writers, compare RunId+generation, preserve the last good
    // generation, and return Committed only after the advertised durability has completed.
    // A disk/IDBFS adapter is deliberately absent; RAM is never advertised as a durable flush.
    public interface IM4SessionStore
    {
        M4StoreResult CommitAndFlush(M4StoreCandidate candidate);
        M4StoredSnapshot ReadLatest(RunId runId);
    }
    public sealed class M4InMemorySessionStore : IM4SessionStore
    {
        readonly object gate=new object();M4StoredSnapshot current;
        public M4StoreResult CommitAndFlush(M4StoreCandidate candidate)
        {
            if(candidate==null)throw new ArgumentNullException(nameof(candidate));
            lock(gate)
            {
                if(current!=null&&current.Receipt.Matches(candidate))return new M4StoreResult(M4StoreStatus.Committed,current.Receipt);
                ulong generation=current==null?0:current.Receipt.Generation;
                if(generation!=candidate.ExpectedGeneration||(current!=null&&!current.Receipt.RunId.Equals(candidate.RunId)))return new M4StoreResult(M4StoreStatus.GenerationConflict,null,"Store RunId/generation CAS mismatch.");
                var validated=M4SessionJson.TryDeserialize(candidate.Json,M4RestorePurpose.BoundedContract);
                if(validated.Status!=M4LoadStatus.LoadedBoundedContract)return new M4StoreResult(M4StoreStatus.Failed,null,validated.Reason);
                var receipt=new M4StoreReceipt(candidate.RunId,candidate.Generation,candidate.Stage,candidate.Digest,M4StoreDurability.InMemoryContractOnly);
                current=new M4StoredSnapshot(candidate.Json,receipt);return new M4StoreResult(M4StoreStatus.Committed,receipt);
            }
        }
        public M4StoredSnapshot ReadLatest(RunId id){lock(gate)return current!=null&&current.Receipt.RunId.Equals(id)?current:null;}
    }
}

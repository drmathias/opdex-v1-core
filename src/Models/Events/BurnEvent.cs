using Stratis.SmartContracts;

public struct BurnEvent
{
    [Index] public Address Sender;
    [Index] public Address To;
    public ulong AmountCrs;
    public UInt256 AmountToken;
    public byte EventTypeId;
}
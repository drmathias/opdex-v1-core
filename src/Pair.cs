using Stratis.SmartContracts;
using Stratis.SmartContracts.Standards;

public class OpdexV1Pair : ContractBase, IStandardToken256
{
    private const ulong MinimumLiquidity = 1000;
    private const string TokenSymbol = "OLPT";
    private const string TokenName = "Opdex Liquidity Pool Token";
    private const byte TokenDecimals = 8;
    
    public OpdexV1Pair(ISmartContractState smartContractState, Address token, Address stakeToken) : base(smartContractState)
    {
        Controller = Message.Sender;
        Token = token;
        StakeToken = stakeToken;
    }
    
    public byte Decimals => TokenDecimals;
    
    public string Name => TokenName;
    
    public string Symbol => TokenSymbol;

    public Address Controller
    {
        get => State.GetAddress(nameof(Controller));
        private set => State.SetAddress(nameof(Controller), value);
    }
    
    public Address Token
    {
        get => State.GetAddress(nameof(Token));
        private set => State.SetAddress(nameof(Token), value);
    }

    public Address StakeToken
    {
        get => State.GetAddress(nameof(StakeToken));
        private set => State.SetAddress(nameof(StakeToken), value);
    }
    
    public ulong ReserveCrs
    {
        get => State.GetUInt64(nameof(ReserveCrs));
        private set => State.SetUInt64(nameof(ReserveCrs), value);
    }
    
    public UInt256 ReserveSrc
    {
        get => State.GetUInt256(nameof(ReserveSrc));
        private set => State.SetUInt256(nameof(ReserveSrc), value);
    }
    
    public UInt256 KLast
    {
        get => State.GetUInt256(nameof(KLast));
        private set => State.SetUInt256(nameof(KLast), value);
    }

    public UInt256 TotalSupply 
    {
        get => State.GetUInt256(nameof(TotalSupply));
        private set => State.SetUInt256(nameof(TotalSupply), value);
    }

    public UInt256 TotalWeight
    {
        get => State.GetUInt256(nameof(TotalWeight));
        private set => State.SetUInt256(nameof(TotalWeight), value);
    }

    public UInt256 GetWeight(Address address)
    {
        return State.GetUInt256($"Weight:{address}");
    }
    
    public void SetWeight(Address address, UInt256 weight)
    {
        State.SetUInt256($"Weight:{address}", weight);
    }
    
    public UInt256 GetWeightK(Address address)
    {
        return State.GetUInt256($"WeightK:{address}");
    }
    
    public void SetWeightK(Address address, UInt256 weightK)
    {
        State.SetUInt256($"WeightK:{address}", weightK);
    }

    public UInt256 GetBalance(Address address)
    {
        return State.GetUInt256($"Balance:{address}");
    }

    private void SetBalance(Address address, UInt256 amount)
    {
        State.SetUInt256($"Balance:{address}", amount);
    }

    // IStandardToken256 interface compatibility
    public UInt256 Allowance(Address owner, Address spender)
    {
        return GetAllowance(owner, spender);
    }
    
    public UInt256 GetAllowance(Address owner, Address spender)
    {
        return State.GetUInt256($"Allowance:{owner}:{spender}");
    }

    private void SetAllowance(Address owner, Address spender, UInt256 amount)
    {
        State.SetUInt256($"Allowance:{owner}:{spender}", amount);
    }

    public bool TransferTo(Address to, UInt256 amount)
    {
        return TransferExecute(Message.Sender, to, amount);
    }
    
    public bool TransferFrom(Address from, Address to, UInt256 amount)
    {
        var allowance = GetAllowance(from, Message.Sender);
        
        if (allowance > 0) SetAllowance(from, Message.Sender, allowance - amount);
        
        return TransferExecute(from, to, amount);
    }

    // IStandardToken256 interface compatibility
    public bool Approve(Address spender, UInt256 currentAmount, UInt256 amount)
    {
        return Approve(spender, amount);
    }
    
    public bool Approve(Address spender, UInt256 amount)
    {
        SetAllowance(Message.Sender, spender, amount);
        
        Log(new ApprovalEvent {Owner = Message.Sender, Spender = spender, Amount = amount, EventTypeId = (byte)EventType.ApprovalEvent});
        
        return true;
    }

    public UInt256 Mint(Address to)
    {
        var reserveCrs = ReserveCrs;
        var reserveToken = ReserveSrc;
        var balanceCrs = Balance;
        var balanceToken = GetSrcBalance(Token, Address);
        var amountCrs = balanceCrs - reserveCrs;
        var amountSrc = balanceToken - reserveToken;
        var totalSupply = TotalSupply;
        
        MintFee(reserveCrs, reserveToken);
        
        UInt256 liquidity;
        if (totalSupply == 0)
        {
            liquidity = Sqrt(amountCrs * amountSrc) - MinimumLiquidity;
            MintExecute(Address.Zero, MinimumLiquidity);
        }
        else
        {
            var amountCrsLiquidity = amountCrs * totalSupply / reserveCrs;
            var amountSrcLiquidity = amountSrc * totalSupply / reserveToken;
            liquidity = amountCrsLiquidity > amountSrcLiquidity ? amountSrcLiquidity : amountCrsLiquidity;
        }
        
        Assert(liquidity > 0, "OPDEX: INSUFFICIENT_LIQUIDITY");
        
        MintExecute(to, liquidity);
        Update(balanceCrs, balanceToken);
        
        KLast = ReserveCrs * ReserveSrc;
        
        Log(new MintEvent { AmountCrs = amountCrs, AmountSrc = amountSrc, Sender = Message.Sender, EventTypeId = (byte)EventType.MintEvent });
        
        return liquidity;
    }

    public UInt256[] Burn(Address to)
    {
        var reserveCrs = ReserveCrs;
        var reserveToken = ReserveSrc;
        var address = Address;
        var token = Token;
        var balanceCrs = Balance;
        var balanceToken = GetSrcBalance(token, address);
        var liquidity = GetBalance(address);
        var totalSupply = TotalSupply;
        var amountCrs = (ulong)(liquidity * balanceCrs / totalSupply);
        var amountSrc = liquidity * balanceToken / totalSupply;
        
        Assert(amountCrs > 0 && amountSrc > 0, "OPDEX: INSUFFICIENT_LIQUIDITY_BURNED");
        
        MintFee(reserveCrs, reserveToken);
        BurnExecute(address, liquidity);
        SafeTransfer(to, amountCrs);
        SafeTransferTo(token, to, amountSrc);
        
        balanceCrs = Balance;
        balanceToken = GetSrcBalance(token, address);
        
        Update(balanceCrs, balanceToken);
        
        Log(new BurnEvent { Sender = Message.Sender, To = to, AmountCrs = amountCrs, AmountSrc = amountSrc, EventTypeId = (byte)EventType.BurnEvent });
        
        return new [] {amountCrs, amountSrc};
    }

    public void Swap(ulong amountCrsOut, UInt256 amountSrcOut, Address to)
    {
        var reserveCrs = ReserveCrs;
        var reserveToken = ReserveSrc;
        var token = Token;
        
        Assert(amountCrsOut > 0 ^ amountSrcOut > 0, "OPDEX: INVALID_OUTPUT_AMOUNT");
        Assert(amountCrsOut < reserveCrs && amountSrcOut < reserveToken, "OPDEX: INSUFFICIENT_LIQUIDITY");
        Assert(to != token, "OPDEX: INVALID_TO");
        
        SafeTransfer(to, amountCrsOut);
        SafeTransferTo(token, to, amountSrcOut);
        
        var balanceCrs = Balance;
        var balanceToken = GetSrcBalance(token, Address);
        var crsDifference = reserveCrs - amountCrsOut;
        var amountCrsIn = balanceCrs > crsDifference ? balanceCrs - crsDifference : 0;
        var srcDifference = reserveToken - amountSrcOut;
        var amountSrcIn = balanceToken > srcDifference ? balanceToken - srcDifference : 0;
        
        Assert(amountCrsIn > 0 || amountSrcIn > 0, "OPDEX: INSUFFICIENT_INPUT_AMOUNT");
        
        var balanceCrsAdjusted = (balanceCrs * 1_000) - (amountCrsIn * 3);
        var balanceTokenAdjusted = (balanceToken * 1_000) - (amountSrcIn * 3);
        
        Assert(balanceCrsAdjusted * balanceTokenAdjusted >= reserveCrs * reserveToken * 1_000_000);
        
        Update(balanceCrs, balanceToken);
        
        KLast = ReserveCrs * ReserveSrc;
        
        Log(new SwapEvent { AmountCrsIn = amountCrsIn, AmountCrsOut = amountCrsOut, AmountSrcIn = amountSrcIn,
             AmountSrcOut = amountSrcOut, Sender = Message.Sender, To = to, EventTypeId = (byte)EventType.SwapEvent });
    }

    public void Borrow(ulong amountCrs, UInt256 amountSrc, Address to, string callbackMethod, byte[] data)
    {
        var token = Token;
        
        Assert(to != Address.Zero && to != Address && to != token);
        
        var balanceCrs = Balance;
        var balanceToken = GetSrcBalance(token, Address);
        
        SafeTransferTo(token, to, amountSrc);
        
        var result = Call(to, amountCrs, callbackMethod, new object[] {data});
        
        Assert(result.Success);
        Assert(balanceCrs == Balance && balanceToken == GetSrcBalance(token, Address), "OPDEX: INSUFFICIENT_DEBT_PAID");
    }

    // // Todo: Handle Address Balance = 0 Issues
    // //   - Either stakers can't stake until MintFee is called and this Address has an LP balance
    // //   - Or, part of the initial burned fee of add liquidity is sent to here to allow staking immediately
    // //   - Todo: test scenarios and calculations with initializing immediately with burned fee
    // public void Stake(Address to, UInt256 weight)
    // {
    //     // Todo: How to handle adding on with extra weight
    //     // TransferFrom only if this is to be called directly, else TransferTo - probably should be TransferTo going through controller
    //     // Would mean we check the difference between balance and totalWeight
    //     SafeTransferFrom(StakeToken, Message.Sender, Address, weight);
    //     SetWeight(to, weight);
    //     var weightK = weight * GetBalance(Address) / TotalWeight; // Todo: This will return a floating point number, adjust for sats
    //     SetWeightK(Address, weightK);
    //     // Verify this 99% sure TotalWeight gets updated **after** finding weightK
    //     TotalWeight += weight;
    // }
    //
    // // Todo: Add shared methods, this does some things twice in combination with WithdrawStakingRewards
    // // Todo: Asserts and validations
    // // Todo: Coming in from Controller
    // public void StopStaking(Address to)
    // {
    //     var weight = GetWeight(Message.Sender);
    //     WithdrawStakingRewards(to);
    //     SafeTransferTo(StakeToken, to, weight);
    //     SetWeight(Message.Sender, 0);
    //     SetWeightK(Message.Sender, 0);
    //     TotalWeight -= weight;
    // }
    //
    // // Todo: Add another method, one for withdrawing LP tokens, one for total withdraw from reserves
    // // Todo: In order to not use Message.Sender, we have to expect something sent in the same transaction
    // // - similar to how liquidity pool tokens are expected to be sent back first, in order to burn.
    // public void WithdrawStakingRewards(Address to)
    // {
    //     // Keep staking, withdraw rewards
    //     var totalWeight = TotalWeight;
    //     var weight = GetWeight(Message.Sender);
    //     var weightKLast = GetWeightK(Message.Sender);
    //     var weightK = weight * GetBalance(Address) / totalWeight;
    //     Assert(weightK > weightKLast); // maybe just return;
    //     var rewards = weightK - weightKLast;
    //     TransferExecute(Address, to, rewards);
    //     var updateWeightK = weight * GetBalance(Address) / totalWeight;
    //     SetWeight(Message.Sender, updateWeightK); // Should this recalc? The Address balance would be different
    // }
    //
    // public void WithdrawStakingRewardsAndBurn(Address to)
    // {
    //     WithdrawStakingRewards(Address);
    //     Burn(to);
    // }

    public void Skim(Address to)
    {
        var token = Token;
        var balanceToken = GetSrcBalance(token, Address) - ReserveSrc;
        var balanceCrs = Balance - ReserveCrs;
        
        SafeTransfer(to, balanceCrs);
        SafeTransferTo(token, to, balanceToken);
    }

    public void Sync()
    {
        Update(Balance, GetSrcBalance(Token, Address));
    }

    public byte[][] GetReserves()
    {
        return new [] { Serializer.Serialize(ReserveCrs), Serializer.Serialize(ReserveSrc) };
    }
    
    private void Update(ulong balanceCrs, UInt256 balanceToken)
    {
        ReserveCrs = balanceCrs;
        
        ReserveSrc = balanceToken;
        
        Log(new SyncEvent { ReserveCrs = balanceCrs, ReserveSrc = balanceToken, EventTypeId = (byte)EventType.SyncEvent });
    }
    
    private void MintFee(ulong reserveCrs, UInt256 reserveToken)
    {
        var kLast = KLast;
        
        if (kLast == 0) return;
        
        var rootK = Sqrt(reserveCrs * reserveToken);
        var rootKLast = Sqrt(kLast);
        
        if (rootK <= rootKLast) return;
        
        var numerator = TotalSupply * (rootK - rootKLast);
        var denominator = (rootK * 5) + rootKLast;
        var liquidity = numerator / denominator;
        
        if (liquidity == 0) return;
        
        var feeToResponse = Call(Controller, 0, "get_FeeTo");
        var feeTo = (Address)feeToResponse.ReturnValue;
        
        Assert(feeToResponse.Success && feeTo != Address.Zero, "OPDEX: INVALID_FEE_TO_ADDRESS");
        
        // Todo: Adjust feeTo
        // Staking theoretically would mint to this pairs address
        // That will be problematic for removing liquidity as users transfer LP to this contract
        // then the balance is checked to find out how much to burn etc.
        // Possibly add persistent state check for staking LP tokens and calculate
        // GetBalance(Address) - State.StakingLPT
        MintExecute(feeTo, liquidity);
    }
    
    private void MintExecute(Address to, UInt256 amount)
    {
        TotalSupply += amount;
        
        SetBalance(to, GetBalance(to) + amount);
        
        Log(new TransferEvent { From = Address.Zero, To = to, Amount = amount, EventTypeId = (byte)EventType.TransferEvent });
    }
    
    private UInt256 GetSrcBalance(Address token, Address owner)
    {
        var balanceResponse = Call(token, 0, "GetBalance", new object[] {owner});
        
        Assert(balanceResponse.Success, "OPDEX: INVALID_BALANCE");
        
        return (UInt256)balanceResponse.ReturnValue;
    }
    
    private void BurnExecute(Address from, UInt256 amount)
    {
        SetBalance(from, GetBalance(from) - amount);
        
        TotalSupply -= amount;
        
        Log(new TransferEvent { From = from, To = Address.Zero, Amount = amount, EventTypeId = (byte)EventType.TransferEvent });
    }
    
    private bool TransferExecute(Address from, Address to, UInt256 amount)
    {
        SetBalance(from, GetBalance(from) - amount);
        SetBalance(to, GetBalance(to) + amount);
        
        Log(new TransferEvent {From = from, To = to, Amount = amount, EventTypeId = (byte)EventType.TransferEvent});
        
        return true;
    }
    
    private static UInt256 Sqrt(UInt256 value)
    {
        if (value <= 3) return 1;
        
        var result = value;
        var root = value / 2 + 1;
        
        while (root < result) 
        {
            result = root;
            root = (value / root + root) / 2;
        }
        
        return result;
    }
}
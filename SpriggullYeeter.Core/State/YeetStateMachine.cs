namespace SpriggullYeeter.Core.State
{
    /// <summary>
    /// Guarded state machine governing Idle -> Held -> Airborne -> Recovering -> Idle transitions.
    /// </summary>
    public sealed class YeetStateMachine
    {
        public YeetState State { get; private set; } = YeetState.Idle;
        public double StateEnteredTime { get; private set; }

        public bool TryBeginHold(double nowSeconds)
        {
            if (State == YeetState.Held)
                return false;

            State = YeetState.Held;
            StateEnteredTime = nowSeconds;
            return true;
        }

        public bool TryLaunch(double nowSeconds)
        {
            if (State != YeetState.Held)
                return false;

            State = YeetState.Airborne;
            StateEnteredTime = nowSeconds;
            return true;
        }

        public bool TryAbortHold(double nowSeconds)
        {
            if (State != YeetState.Held)
                return false;

            State = YeetState.Idle;
            StateEnteredTime = nowSeconds;
            return true;
        }

        public bool TryBeginRecovery(double nowSeconds)
        {
            if (State != YeetState.Airborne)
                return false;

            State = YeetState.Recovering;
            StateEnteredTime = nowSeconds;
            return true;
        }

        public void CompleteRecovery(double nowSeconds)
        {
            State = YeetState.Idle;
            StateEnteredTime = nowSeconds;
        }

        public void ForceReset()
        {
            State = YeetState.Idle;
            StateEnteredTime = 0d;
        }
    }
}

using System.Collections.Generic;
using GUZ.Core.Extensions;
using GUZ.Core.Services.Vm;
using MyBox;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace GUZ.Core.Models.Proxy
{
    /// <summary>
    /// Pre-Cache FightAiMoves for better debug capabilities. Otherwise, it needs to be fetched at each AI_Attack() call.
    /// </summary>
    public class FightAiProxy
    {
        [Inject] private readonly VmService _vmService;
        
        private readonly FightAiInstance _fightAiInstance;

        public readonly int MoveCount;
        public readonly FightAiMove[] Moves;
        public FightAiMove GetRandomMove()
        {
            if (Moves.Length == 0)
                return FightAiMove.Nop;
            else
                return Moves.GetRandom();
        }

        public FightAiProxy(FightAiInstance fightAiInstance)
        {
            this.Inject();

            _fightAiInstance = fightAiInstance;

            CalculateAttackMoves(out MoveCount, out Moves);
        }

        /// <summary>
        /// Caching of Moves so that we don't need to recalculate them with each usage.
        /// </summary>
        private void CalculateAttackMoves(out int moveCount, out FightAiMove[] moves)
        {
            var tempMoves = new List<FightAiMove>();
            
            for (moveCount = 0; moveCount < _vmService.FightAiMoveMax; moveCount++)
            {
                // Load all move entries in list until the value is 0 aka unset.
                if (_fightAiInstance.GetMove(moveCount) == FightAiMove.Nop)
                    break;
                
                tempMoves.Add(_fightAiInstance.GetMove(moveCount));
            }
            
            moves = tempMoves.ToArray();
        }
    }
}

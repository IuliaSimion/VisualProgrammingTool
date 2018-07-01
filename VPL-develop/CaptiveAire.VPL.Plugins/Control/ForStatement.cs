using CaptiveAire.VPL.Extensions;
using CaptiveAire.VPL.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CaptiveAire.VPL.Plugins.Control
{
    public class ForStatement : CompoundStatement
    {
        private readonly IParameter From;
        private readonly IParameter To;
        private readonly IParameter Step;
        private readonly IBlock Block;

        public ForStatement(IElementCreationContext context)
            : base(context)
        {
            From = Owner.CreateParameter("from", Owner.GetFloatType(), null, " : ");
            To = Owner.CreateParameter("to", Owner.GetFloatType(), null, " ; ");
            Step = Owner.CreateParameter("step", Owner.GetFloatType(), "step : ");

            Block = Owner.CreateBlock("block", "For");

            Block.Parameters.Add(From);
            Block.Parameters.Add(To);
            Block.Parameters.Add(Step);

            Blocks.Add(Block);
        }

        protected override async Task ExecuteCoreAsync(IExecutionContext executionContext, CancellationToken cancellationToken)
        {
            double from = (await From.EvaluateAsync(executionContext, cancellationToken)).GetConvertedValue<double>();
            double to = (await To.EvaluateAsync(executionContext, cancellationToken)).GetConvertedValue<double>();
            double step = (await Step.EvaluateAsync(executionContext, cancellationToken)).GetConvertedValue<double>();

            if (from <= to)
            {
                for (double index = from; index < to; index += step)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await Block.ExecuteAsync(executionContext, cancellationToken);
                }
            }
            else
            {
                for (double index = from; index > to; index -= step)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await Block.ExecuteAsync(executionContext, cancellationToken);
                }
            }
        }

    }
}

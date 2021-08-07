using GeneticSharp.Domain.Chromosomes;
using Jtc.Optimization.LeanOptimizer.Base;
using Jtc.Optimization.Objects.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jtc.Optimization.LeanOptimizer
{


    /// <summary>
    /// Adaptive fitness that increases period proportional to improvement in sharpe ratio
    /// </summary>
    public class AdaptiveSharpeRatioFitness : OptimizerFitness
    {

        private double _previousFitness = (double)ErrorRatio;

        public AdaptiveSharpeRatioFitness(IOptimizerConfiguration config, IFitnessFilter filter) : base(config, filter)
        {
        }

        public override double Evaluate(IChromosome chromosome)
        {
            var fitness = EvaluateBase(chromosome);

            //fitness has improved: adapt the period to steepen ascent. Don't adapt on degenerate negative return
            if (_previousFitness > 0 && fitness > _previousFitness && Config.StartDate.HasValue)
            {
                var hours = Config.EndDate.Value.AddDays(1).AddTicks(-1).Subtract(Config.StartDate.Value).TotalHours;
                var improvement = fitness / _previousFitness;
                var adding = hours - (hours * improvement);
                //todo: after config is modified, executions in process will still return for previous dates
                Config.StartDate = Config.StartDate.Value.AddHours(adding);

                //restart with longer in sample. History will now be ignored
                //todo: retain history for failure (-10 Sharpe) executions			
                //resample current alpha
                _previousFitness = EvaluateBase(chromosome);
                fitness = _previousFitness;
            }

            if (fitness > _previousFitness)
            {
                _previousFitness = fitness;
            }
            return fitness;
        }

        protected virtual double EvaluateBase(IChromosome chromosome)
        {
            return base.Evaluate(chromosome);
        }

        protected void ExtendFailureKeys(DateTime extending)
        {
            var failures = ResultMediator.GetResults(AppDomain.CurrentDomain).Where(r => r.Value["SharpeRatio"] == ErrorRatio);

            var previousKey = JsonConvert.SerializeObject(Config.StartDate);
            var extendingKey = JsonConvert.SerializeObject(extending);

            var switching = new List<Tuple<string, string>>();

            foreach (var item in failures)
            {
                var after = item.Key.Replace(previousKey, extendingKey);
                switching.Add(Tuple.Create(item.Key, after));
            }

            foreach (var item in switching)
            {
                var before = ResultMediator.GetResults(AppDomain.CurrentDomain)[item.Item1];
                ResultMediator.GetResults(AppDomain.CurrentDomain).Remove(item.Item1);
                ResultMediator.GetResults(AppDomain.CurrentDomain).Add(item.Item2, before);
            }

        }

    }
}

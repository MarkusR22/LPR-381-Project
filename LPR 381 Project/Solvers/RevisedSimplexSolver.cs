using LPR_381_Project.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LPR_381_Project.Utils;

namespace LPR_381_Project.Solvers
{
    public class RevisedSimplexSolver
    {
        private const double TOLERANCE = 1e-9;
        private List<double[,]> Iterations { get; }

        public RevisedSimplexSolver()
        {
            Iterations = new List<double[,]>();
        }

        public List<double[,]> Solve(LinearModel model)
        {
            Iterations.Clear();

            // Build initial tableau
            double[,] tableau = BuildInitialTableau(model);
            Iterations.Add(Clone(tableau));

            int rows = tableau.GetLength(0);
            int cols = tableau.GetLength(1);
            int numVars = model.Variables.Count;

            while (true)
            {
                // Find entering variable (most negative in objective row)
                int pivotCol = -1;
                double mostNegative = -TOLERANCE;

                for (int j = 0; j < cols - 1; j++)
                {
                    if (tableau[0, j] < mostNegative)
                    {
                        mostNegative = tableau[0, j];
                        pivotCol = j;
                    }
                }

                if (pivotCol == -1) break; // Optimal reached

                // Find leaving variable (minimum ratio)
                int pivotRow = -1;
                double minRatio = double.PositiveInfinity;

                for (int i = 1; i < rows; i++)
                {
                    if (tableau[i, pivotCol] > TOLERANCE)
                    {
                        double ratio = tableau[i, cols - 1] / tableau[i, pivotCol];
                        if (ratio < minRatio)
                        {
                            minRatio = ratio;
                            pivotRow = i;
                        }
                    }
                }

                if (pivotRow == -1)
                    throw new InvalidOperationException("Unbounded Linear Program");

                // Pivot
                Pivot(tableau, pivotRow, pivotCol);
                Iterations.Add(Clone(tableau));
            }

            // Write all iterations to file
            OutputWriter.WriteIterations("result_revised.txt", Iterations);

            return Iterations;
        }

        private double[,] BuildInitialTableau(LinearModel model)
        {
            int numVars = model.Variables.Count;
            int numConstraints = model.Constraints.Count;
            int rows = numConstraints + 1;
            int cols = numVars + numConstraints + 1;

            double[,] tableau = new double[rows, cols];

            // Objective function (row 0)
            for (int j = 0; j < numVars; j++)
            {
                tableau[0, j] = -model.Variables[j].Coefficient;
            }

            // Constraints
            for (int i = 0; i < numConstraints; i++)
            {
                Constraint c = model.Constraints[i];
                for (int j = 0; j < numVars; j++)
                    tableau[i + 1, j] = c.Coefficients[j];

                tableau[i + 1, numVars + i] = 1; // Slack variable
                tableau[i + 1, cols - 1] = c.RHS;
            }

            return tableau;
        }

        private void Pivot(double[,] T, int pivotRow, int pivotCol)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            double pivotValue = T[pivotRow, pivotCol];

            // Normalize pivot row
            for (int j = 0; j < cols; j++)
            {
                T[pivotRow, j] /= pivotValue;
            }

            // Eliminate pivot column in other rows
            for (int i = 0; i < rows; i++)
            {
                if (i == pivotRow) continue;
                double factor = T[i, pivotCol];
                for (int j = 0; j < cols; j++)
                {
                    T[i, j] -= factor * T[pivotRow, j];
                }
            }

            ZeroSmall(T);
        }

        private void ZeroSmall(double[,] T)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    if (Math.Abs(T[i, j]) < TOLERANCE)
                        T[i, j] = 0;
        }

        private double[,] Clone(double[,] source)
        {
            int rows = source.GetLength(0);
            int cols = source.GetLength(1);
            var copy = new double[rows, cols];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}

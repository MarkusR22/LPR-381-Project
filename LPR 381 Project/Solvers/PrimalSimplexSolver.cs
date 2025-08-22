using LPR_381_Project.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LPR_381_Project.Utils;

namespace LPR_381_Project.Solvers
{
    public class PrimalSimplexSolver
    {
        private List<double[,]> Iterations { get; set; }

        public PrimalSimplexSolver()
        {
            Iterations = new List<double[,]>();
        }

        /// <summary>
        /// Solve a LinearModel using the standard Primal Simplex algorithm
        /// </summary>
        public List<double[,]> Solve(LinearModel model)
        {
            // Step 1: Create initial tableau
            double[,] tableau = CreateTableau(model);

            Iterations.Add(CloneTableau(tableau));

            // Step 2: Main loop
            while (!IsOptimal(tableau))
            {
                int pivotCol = ChoosePivotColumn(tableau);
                if (pivotCol < 0)
                    break;

                int pivotRow = ChoosePivotRow(tableau, pivotCol);
                if (pivotRow < 0)
                    throw new InvalidOperationException("Unbounded solution detected");

                Pivot(tableau, pivotRow, pivotCol);
                Iterations.Add(CloneTableau(tableau));
            }

            // Step 3: Write iterations to Output folder
            LPR_381_Project.Utils.OutputWriter.WriteIterations("result_primal.txt", Iterations);

            return Iterations;
        }

        #region Tableau Operations

        private double[,] CreateTableau(LinearModel model)
        {
            int numConstraints = model.Constraints.Count;
            int numVariables = model.Variables.Count;
            int rows = numConstraints + 1;
            int cols = numVariables + numConstraints + 1; // slack vars + RHS

            double[,] T = new double[rows, cols];

            // Objective row (top row)
            for (int j = 0; j < numVariables; j++)
            {
                T[0, j] = (model.Obj == Objective.Maximize ? -1 : 1) * model.Variables[j].Coefficient;
            }

            // Constraints
            for (int i = 0; i < numConstraints; i++)
            {
                Constraint c = model.Constraints[i];
                for (int j = 0; j < numVariables; j++)
                {
                    T[i + 1, j] = c.Coefficients[j];
                }

                // Slack variable
                T[i + 1, numVariables + i] = 1;

                // RHS
                T[i + 1, cols - 1] = c.RHS;
            }

            return T;
        }

        private bool IsOptimal(double[,] T)
        {
            int cols = T.GetLength(1);
            for (int j = 0; j < cols - 1; j++)
            {
                if (T[0, j] < 0)
                    return false;
            }
            return true;
        }

        private int ChoosePivotColumn(double[,] T)
        {
            int cols = T.GetLength(1);
            double min = 0;
            int pivotCol = -1;

            for (int j = 0; j < cols - 1; j++)
            {
                if (T[0, j] < min)
                {
                    min = T[0, j];
                    pivotCol = j;
                }
            }
            return pivotCol;
        }

        private int ChoosePivotRow(double[,] T, int pivotCol)
        {
            int rows = T.GetLength(0);
            int rhsCol = T.GetLength(1) - 1;
            double minRatio = double.MaxValue;
            int pivotRow = -1;

            for (int i = 1; i < rows; i++)
            {
                double value = T[i, pivotCol];
                if (value > 0)
                {
                    double ratio = T[i, rhsCol] / value;
                    if (ratio < minRatio)
                    {
                        minRatio = ratio;
                        pivotRow = i;
                    }
                }
            }

            return pivotRow;
        }

        private void Pivot(double[,] T, int pivotRow, int pivotCol)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);

            double pivot = T[pivotRow, pivotCol];

            // Normalize pivot row
            for (int j = 0; j < cols; j++)
            {
                T[pivotRow, j] /= pivot;
            }

            // Eliminate other rows
            for (int i = 0; i < rows; i++)
            {
                if (i != pivotRow)
                {
                    double factor = T[i, pivotCol];
                    for (int j = 0; j < cols; j++)
                    {
                        T[i, j] -= factor * T[pivotRow, j];
                    }
                }
            }
        }

        private double[,] CloneTableau(double[,] T)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            double[,] clone = new double[rows, cols];
            Array.Copy(T, clone, T.Length);
            return clone;
        }

        #endregion
    }
}

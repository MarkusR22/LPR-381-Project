using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LPR_381_Project.Solvers
{
    class DualSimplex
    {

        private List<double[,]> Iterations { get; }

        private double Tolerance { get; set; } = 1e-9;

        public DualSimplex()
        {
            Iterations = new List<double[,]>();
        }

        //Solving using Dual Simplex and returning list of all iterations
        public List<double[,]> Solve(double[,] initialTableau)
        {

            Iterations.Clear();
            Iterations.Add(initialTableau);

            var T = Clone(initialTableau);

            //Looping until there are no more negatives in the RHS
            while (!AllRhsNonNegative(T))
            {

                int pivotRow = ChoosePivotRow(T);

                if (pivotRow < 0)
                {
                    break;
                }

                int pivotColumn = ChoosePivotColumn(T, pivotRow);

                if (pivotColumn < 0)
                {
                    throw new InvalidOperationException("Dual Simplex: Infeasible (no negative in pivot row)");
                }

                Pivot(T, pivotRow, pivotColumn);
                Iterations.Add(Clone(T));
            }

            return Iterations;
        }


        //Checking if there is negatives in RHS
        private bool AllRhsNonNegative(double[,] T)
        {
            int rows = T.GetLength(0);
            int rhsCol = T.GetLength(1) - 1;

            for (int i = 1; i < rows; i++)
            {
                if (T[i, rhsCol] < -Tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        //Finding pivot row (row with greatest negative value)
        private int ChoosePivotRow(double[,] T)
        {
            int rows = T.GetLength(0);
            int rhsCol = T.GetLength(1) - 1;

            int selectedRow = -1;

            double minVal = double.PositiveInfinity;

            for (int i = 1; i < rows; i++)
            {
                double rhsValue = T[i, rhsCol];

                if (rhsValue < minVal - Tolerance)
                {
                    minVal = rhsValue;
                    selectedRow = i;
                }

            }

            //Returning the row index if the smallest RHS value is negative, otherwise returning -1
            return (minVal < -Tolerance) ? selectedRow : -1;
        }


        //Finding the pivot column (column with the smallest ratio between z and pivot row cell)
        private int ChoosePivotColumn(double[,] T, int pivotRow)
        {
            int cols = T.GetLength(1);
            int rhsCol = cols - 1;

            int selectedCol = -1;

            double smallestRatio = double.PositiveInfinity;

            for (int i = 0; i < rhsCol; i++)
            {

                double pivotRowCell = T[pivotRow, i];

                //Calculating ratio's only for columns where the cell is negative in the pivot row
                if (pivotRowCell < -Tolerance)
                {
                    double zCell = T[0, i];
                    double ratio = Math.Abs(zCell / pivotRowCell);
                    if (ratio < smallestRatio - Tolerance || (Math.Abs(ratio - smallestRatio) <= Tolerance && (selectedCol < 0 || i < selectedCol)))
                    {
                        smallestRatio = ratio;
                        selectedCol = i;
                    }
                }
            }
            return selectedCol;



        }

        //Pivoting and creating new table
        private void Pivot(double[,] T, int pivotRow, int pivotCol)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);

            double pivotCell = T[pivotRow, pivotCol];
            
            for (int i = 0; i < cols; i++)
            {
                T[pivotRow, i] /= pivotCell;
            }

            for (int i = 0; i < rows; i++)
            {
                if (i == pivotRow)
                {
                    continue;
                }

                double factor = T[i, pivotCol];

                if (Math.Abs(factor) <= Tolerance)
                {
                    continue;
                }

                for (int j = 0; j < cols; j++)
                {
                    T[i, j] = T[i, j] - factor * T[pivotRow, j];
                }
            }

            ZeroSmall(T);
        }

        //Making extremely small values = 0
        public void ZeroSmall(double[,] T)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);

            for (int i = 0; i < rows; i ++)
            {
                for (int j = 0; j < cols; j ++)
                {
                    if (Math.Abs(T[i, j]) < Tolerance)
                    {
                        T[i, j] = 0;
                    }
                }
            }
        }

        //Creating a copy of the table
        private static double[,] Clone(double[,] table)
        {
            int rows = table.GetLength(0);
            int cols = table.GetLength(1);
            var copy = new double[rows, cols];
            Array.Copy(table, copy, table.Length);
            return copy;
        }

    }
}

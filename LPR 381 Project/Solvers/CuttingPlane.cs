using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

namespace LPR_381_Project.Solvers
{
    public class CuttingPlane
    {
        public class Result
        {
            public double[] XOpt;
            public double ZOpt;
            public int CutsAdded;
            public List<double[,]> Tableaus;
            public List<string> Logs;
        }


        public double Tolerance { get; set; } = 1e-9;
        public int MaxIterations { get; set; } = 10_000;
        public int MaxCuts { get; set; } = 200;

        public Result Solve(double[,] A, double[] b, double[] c, bool[] isInteger)
        {
            if (A == null) throw new ArgumentNullException(nameof(A));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (isInteger == null) throw new ArgumentNullException(nameof(isInteger));


            int m = A.GetLength(0);
            int n = A.GetLength(1);
            if (b.Length != m) throw new ArgumentException("b length ≠ rows(A)");
            if (c.Length != n) throw new ArgumentException("c length ≠ cols(A)");
            if (isInteger.Length != n) throw new ArgumentException("isInteger length ≠ cols(A)");
            for (int i = 0; i < m; i++) if (b[i] < -Tolerance) throw new ArgumentException("RHS b must be ≥ 0 for initial primal BFS. Preprocess rows.");


            int cols = n + m + 1; // vars + slacks + RHS
            double[,] T = new double[m + 1, cols];


            for (int j = 0; j < n; j++) T[0, j] = -c[j]; // -c for max
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < n; j++) T[i + 1, j] = A[i, j];
                T[i + 1, n + i] = 1.0; // slack
                T[i + 1, cols - 1] = b[i];
            }


            var basis = new int[m];
            for (int i = 0; i < m; i++) basis[i] = n + i;


            var allTableaus = new List<double[,]> { CloneMatrix(T) };
            var logs = new List<string>();


            logs.Add("=== PRIMAL SIMPLEX: LP relaxation ===");
            RunPrimalSimplex(T, basis, allTableaus, logs);


            int cuts = 0;
            while (true)
            {
                var x = ReadCurrentX(T, basis, n);
                int fractIdx = -1;
                for (int j = 0; j < n; j++)
                {
                    if (isInteger[j])
                    {
                        double frac = FracPart(x[j]);
                        if (frac > 1e-7 && 1.0 - frac > 1e-7) { fractIdx = j; break; }
                    }
                }


                if (fractIdx < 0)
                {
                    double z = T[0, cols - 1];
                    logs.Add("All integer – cutting-plane loop terminates.");
                    return new Result { XOpt = x, ZOpt = z, CutsAdded = cuts, Tableaus = allTableaus, Logs = logs };
                }

                if (cuts >= MaxCuts) throw new InvalidOperationException($"Reached MaxCuts={MaxCuts} but still fractional.");


                int rowToCut = -1;
                for (int i = 0; i < basis.Length; i++)
                {
                    if (basis[i] == fractIdx)
                    {
                        double rhs = T[i + 1, T.GetLength(1) - 1];
                        if (IsFrac(rhs)) { rowToCut = i + 1; break; }
                    }
                }
                if (rowToCut < 0)
                {
                    for (int i = 0; i < basis.Length; i++)
                    {
                        int colIdx = basis[i];
                        if (colIdx < n && isInteger[colIdx])
                        {
                            double rhs = T[i + 1, T.GetLength(1) - 1];
                            if (IsFrac(rhs)) { rowToCut = i + 1; break; }
                        }
                    }
                }


                if (rowToCut < 0)
                {
                    for (int i = 1; i < T.GetLength(0); i++)
                    {
                        double rhs = T[i, T.GetLength(1) - 1];
                        if (IsFrac(rhs)) { rowToCut = i; break; }
                    }
                }
                if (rowToCut < 0)
                {
                    double z = T[0, T.GetLength(1) - 1];
                    logs.Add("No fractional RHS row found; stopping.");
                    return new Result { XOpt = ReadCurrentX(T, basis, n), ZOpt = z, CutsAdded = cuts, Tableaus = allTableaus, Logs = logs };
                }


                int oldCols = T.GetLength(1);
                int oldRows = T.GetLength(0);
                double rhsRow = T[rowToCut, oldCols - 1];
                double bbar = FracPart(rhsRow);
                if (bbar <= Tolerance || 1.0 - bbar <= Tolerance)
                {
                    logs.Add($"[Cut skipped] Degenerate fractional part on row {rowToCut} (RHS={rhsRow}).");
                    continue;
                }

                var Tnew = new double[oldRows + 1, oldCols + 1];
                for (int i = 0; i < oldRows; i++)
                    for (int j = 0; j < oldCols; j++)
                        Tnew[i, j] = T[i, j];


                Tnew[0, oldCols] = 0.0; // new slack not in obj


                int newRow = oldRows;
                int newSlackCol = oldCols;


                for (int j = 0; j < oldCols - 1; j++)
                {
                    double a = T[rowToCut, j];
                    double coeff = -FracPart(-a);
                    if (Math.Abs(coeff) < 1e-12) coeff = 0.0;
                    Tnew[newRow, j] = coeff;
                }
                Tnew[newRow, newSlackCol] = 1.0; // slack
                Tnew[newRow, oldCols - 1] = -bbar; // RHS negative ⇒ dual-simplex friendly

                T = Tnew;
                basis = basis.Concat(new[] { newSlackCol }).ToArray();


                allTableaus.Add(CloneMatrix(T));
                cuts++;
                logs.Add($"=== Added Gomory cut #{cuts} from row {rowToCut} (b̄={bbar:0.###}) – starting DUAL SIMPLEX ===");


                RunDualSimplex(T, basis, allTableaus, logs);
            }
        }

        private void RunPrimalSimplex(double[,] T, int[] basis, List<double[,]> allTableaus, List<string> logs)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            int iterations = 0;
            while (true)
            {
                iterations++;
                if (iterations > MaxIterations) throw new InvalidOperationException("Primal Simplex: iteration limit reached.");


                int enter = -1; double minRC = +0.0;
                for (int j = 0; j < cols - 1; j++)
                {
                    double rc = T[0, j];
                    if (rc < minRC - Tolerance) { minRC = rc; enter = j; }
                }
                if (enter < 0)
                {
                    logs.Add("[Primal] Optimal (no negative reduced costs).");
                    break;
                }

                int leave = -1; double bestRatio = double.PositiveInfinity;
                for (int i = 1; i < rows; i++)
                {
                    double aij = T[i, enter];
                    if (aij > Tolerance)
                    {
                        double rhs = T[i, cols - 1];
                        double theta = rhs / aij;
                        if (theta < bestRatio - 1e-12) { bestRatio = theta; leave = i; }
                    }
                }
                if (leave < 0) throw new InvalidOperationException("Primal Simplex: Unbounded (no valid leaving row).");


                LogIter(T, basis, "Primal", iterations, enter, leave, logs);
                DoPivot(T, leave, enter);
                basis[leave - 1] = enter;
                allTableaus.Add(CloneMatrix(T));
            }
        }

        private void RunDualSimplex(double[,] T, int[] basis, List<double[,]> allTableaus, List<string> logs)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            int iterations = 0;
            while (true)
            {
                iterations++;
                if (iterations > MaxIterations) throw new InvalidOperationException("Dual Simplex: iteration limit reached.");


                int leave = -1; double minRhs = +0.0;
                for (int i = 1; i < rows; i++)
                {
                    double rhs = T[i, cols - 1];
                    if (rhs < minRhs - Tolerance) { minRhs = rhs; leave = i; }
                }
                if (leave < 0)
                {
                    logs.Add("[Dual] Feasible again (all RHS ≥ 0). Done.");
                    break;
                }

                int enter = -1; double best = double.PositiveInfinity;
                for (int j = 0; j < cols - 1; j++)
                {
                    double a = T[leave, j];
                    if (a < -Tolerance)
                    {
                        double rc = T[0, j];
                        double ratio = rc / a;
                        if (ratio < best - 1e-12) { best = ratio; enter = j; }
                    }
                }
                if (enter < 0) throw new InvalidOperationException("Dual Simplex: Infeasible (no entering col with negative in pivot row).");


                LogIter(T, basis, "Dual", iterations, enter, leave, logs);
                DoPivot(T, leave, enter);
                basis[leave - 1] = enter;
                allTableaus.Add(CloneMatrix(T));
            }
        }

        private void DoPivot(double[,] T, int pivotRow, int pivotCol)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            double piv = T[pivotRow, pivotCol];
            if (Math.Abs(piv) < 1e-14) throw new InvalidOperationException("Zero pivot encountered.");


            for (int j = 0; j < cols; j++) T[pivotRow, j] /= piv;
            for (int i = 0; i < rows; i++)
            {
                if (i == pivotRow) continue;
                double f = T[i, pivotCol];
                if (Math.Abs(f) <= 1e-14) continue;
                for (int j = 0; j < cols; j++) T[i, j] -= f * T[pivotRow, j];
            }
            ZeroSmallEntries(T);
        }

        private void ZeroSmallEntries(double[,] T)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    if (Math.Abs(T[i, j]) < 1e-12) T[i, j] = 0.0;
        }


        private void LogIter(double[,] T, int[] basis, string phase, int iter, int enter, int leave, List<string> logs)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            int m = basis.Length;
            int nTotal = cols - 1;

            var Bcols = new int[m];
            for (int i = 0; i < m; i++) Bcols[i] = basis[i];
            var B = new double[m, m];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < m; j++)
                    B[i, j] = T[i + 1, Bcols[j]];
            var Binv = InvertSmallMatrix(B);


            string rc = string.Join(", ", Enumerable.Range(0, nTotal).Select(j => $"rc[{j}]={T[0, j]:0.###}"));
            logs.Add($"[{phase}] it{iter}: enter col {enter}, leave row {leave} | z={T[0, cols - 1]:0.###} B ^ -1 = {MatrixToString(Binv)} Reduced costs: {rc}");
        }

        private string MatrixToString(double[,] M)
        {
            int r = M.GetLength(0), c = M.GetLength(1);
            var lines = new List<string>();
            for (int i = 0; i < r; i++)
            {
                var row = new List<string>();
                for (int j = 0; j < c; j++) row.Add(M[i, j].ToString("0.###", CultureInfo.InvariantCulture));
                lines.Add(" " + string.Join(" ", row));
            }
            return string.Join(" ", lines);
        }


        private static double[,] CloneMatrix(double[,] src)
        {
            int r = src.GetLength(0), c = src.GetLength(1);
            var dst = new double[r, c];
            Array.Copy(src, dst, src.Length);
            return dst;
        }

        private static double FracPart(double x)
        {
            double f = x - Math.Floor(x);
            if (Math.Abs(f - 1.0) < 1e-12) f = 0.0;
            return f;
        }
        private static bool IsFrac(double x)
        {
            double f = FracPart(Math.Abs(x));
            return f > 1e-7 && 1.0 - f > 1e-7;
        }


        private static double[] ReadCurrentX(double[,] T, int[] basis, int nOrig)
        {
            int m = basis.Length;
            int cols = T.GetLength(1);
            var x = new double[nOrig];
            for (int i = 0; i < m; i++)
            {
                int col = basis[i];
                if (col < nOrig) x[col] = T[i + 1, cols - 1];
            }

            for (int j = 0; j < x.Length; j++) if (Math.Abs(x[j]) < 1e-12) x[j] = 0.0;
            return x;
        }


        private static double[,] InvertSmallMatrix(double[,] A)
        {
            int n = A.GetLength(0);
            if (n != A.GetLength(1)) throw new ArgumentException("Matrix must be square");
            var M = new double[n, 2 * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) M[i, j] = A[i, j];
                M[i, i + n] = 1.0;
            }
            for (int i = 0; i < n; i++)
            {
                int piv = i;
                for (int r = i + 1; r < n; r++) if (Math.Abs(M[r, i]) > Math.Abs(M[piv, i])) piv = r;
                if (Math.Abs(M[piv, i]) < 1e-14) throw new InvalidOperationException("Singular matrix while building B^{-1} for log.");
                if (piv != i) SwapRows(M, i, piv);


                double diag = M[i, i];
                for (int j = 0; j < 2 * n; j++) M[i, j] /= diag;
                for (int r = 0; r < n; r++)
                {
                    if (r == i) continue;
                    double f = M[r, i];
                    if (Math.Abs(f) <= 1e-14) continue;
                    for (int j = 0; j < 2 * n; j++) M[r, j] -= f * M[i, j];
                }
            }
            var inv = new double[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) inv[i, j] = M[i, j + n];
            return inv;
        }

        private static void SwapRows(double[,] M, int r1, int r2)
        {
            int c = M.GetLength(1);
            for (int j = 0; j < c; j++) { double tmp = M[r1, j]; M[r1, j] = M[r2, j]; M[r2, j] = tmp; }
        }


        // Quick parser for the brief's example format
        public Result SolveFromBriefFormat(string[] lines)
        {
            lines = lines.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
            if (lines.Length < 3) throw new ArgumentException("Need at least 3 lines: objective, ≥1 constraint, sign row");


            var objTokens = lines[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            bool isMax = objTokens[0].Equals("max", StringComparison.OrdinalIgnoreCase);
            int n = objTokens.Length - 1;
            var c = new double[n];
            for (int j = 0; j < n; j++) c[j] = ParseSigned(objTokens[j + 1]);


            var signTokens = lines[lines.Length - 1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (signTokens.Length != n) throw new ArgumentException("Sign row must have one token per variable.");
            var isInt = signTokens.Select(tok => tok.Equals("bin", StringComparison.OrdinalIgnoreCase) || tok.Equals("int", StringComparison.OrdinalIgnoreCase)).ToArray();


            var constr = new List<double[]>();
            var rhsList = new List<double>();
            for (int i = 1; i < lines.Length - 1; i++)
            {
                var tks = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                var coeffs = new double[n];
                for (int j = 0; j < n; j++) coeffs[j] = ParseSigned(tks[j]);
                string rel = tks[n];
                double rhsVal = double.Parse(tks[n + 1], CultureInfo.InvariantCulture);


                if (rel == ">=") { for (int j = 0; j < n; j++) coeffs[j] *= -1; rhsVal *= -1; rel = "<="; }
                if (rel != "<=") throw new NotSupportedException("Quick parser supports <= only (or >= which is flipped). Use your general parser otherwise.");
                constr.Add(coeffs); rhsList.Add(rhsVal);
            }

            var A = new double[constr.Count, n];
            for (int i = 0; i < constr.Count; i++) for (int j = 0; j < n; j++) A[i, j] = constr[i][j];
            var bArr = rhsList.ToArray();


            if (!isMax) { for (int j = 0; j < n; j++) c[j] *= -1; }
            var res = Solve(A, bArr, c, isInt);
            if (!isMax) { res.ZOpt *= -1; }
            return res;
        }


        private static double ParseSigned(string token)
        {
            token = token.Trim();
            return double.Parse(token, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
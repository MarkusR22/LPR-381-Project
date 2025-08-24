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


            // If any RHS is negative, use Dual Simplex to restore feasibility
            bool hasNegativeRhs = false;
            int rowsT = T.GetLength(0), colsT = T.GetLength(1);
            for (int i = 1; i < rowsT; i++)
            {
                if (T[i, colsT - 1] < -Tolerance) { hasNegativeRhs = true; break; }
            }

            if (hasNegativeRhs)
            {
                // Log and fix feasibility first
                logs.Add("=== INITIAL DUAL SIMPLEX: fixing negative RHS ===");
                RunDualSimplex(T, basis, allTableaus, logs);
            }

            // proceed with LP relaxation using Primal Simplex
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
        // Robust parser that accepts "<= 40" and "<=40" (and >=, =<, =>)
        // C# 7.3 compatible (no index-from-end operator and no newer features)
        public Result SolveFromBriefFormat(string[] rawLines)
        {
            if (rawLines == null || rawLines.Length == 0)
                throw new ArgumentException("Input is empty.");

            // Normalize: trim, drop blanks and comment lines
            var normalized = new List<Tuple<string, int>>(); // (line, originalLineNumber)
            for (int i = 0; i < rawLines.Length; i++)
            {
                string s = (rawLines[i] ?? "").Trim();
                if (s.Length == 0) continue;
                if (s.StartsWith("#") || s.StartsWith("//")) continue;
                normalized.Add(Tuple.Create(s, i + 1));
            }

            if (normalized.Count < 3)
                throw new ArgumentException("Need at least 3 non-empty lines: objective, one constraint, and the sign row.");

            // Objective
            string[] obj = normalized[0].Item1.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (obj.Length < 2)
                throw new FormatException("Objective line must be: 'max|min c1 c2 ...'.");

            bool isMax = string.Equals(obj[0], "max", StringComparison.OrdinalIgnoreCase);
            bool isMin = string.Equals(obj[0], "min", StringComparison.OrdinalIgnoreCase);
            if (!isMax && !isMin)
                throw new FormatException("Objective line must start with 'max' or 'min'.");

            int n = obj.Length - 1;
            var c = new double[n];
            for (int j = 0; j < n; j++) c[j] = ParseSigned(obj[j + 1]);

            // Sign row is the last normalized line (no [^1] usage)
            int lastIdx = normalized.Count - 1;
            string[] sign = normalized[lastIdx].Item1.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (sign.Length != n)
                throw new FormatException("Sign row must have exactly the same number of tokens as variables.");

            var isInt = new bool[n];
            for (int j = 0; j < n; j++)
            {
                string tok = sign[j].ToLowerInvariant();
                isInt[j] = (tok == "int" || tok == "bin"); // others treated as continuous here
            }

            // Constraints: lines 1 .. lastIdx-1
            var constr = new List<double[]>();
            var rhsList = new List<double>();
            for (int k = 1; k <= lastIdx - 1; k++)
            {
                string s = normalized[k].Item1;
                int originalLine = normalized[k].Item2;

                var tks = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (tks.Count < n + 1)
                    throw new FormatException("Constraint must have n coeffs plus relation/RHS.");

                // coefficients
                var coeffs = new double[n];
                for (int j = 0; j < n; j++) coeffs[j] = ParseSigned(tks[j]);

                string rel;
                string rhsTok;

                if (tks.Count == n + 1)
                {
                    // Relation and RHS glued together, e.g. <=40 or =>-5
                    SplitRelRhsCombined_CSharp7(tks[n], originalLine, out rel, out rhsTok);
                }
                else
                {
                    rel = tks[n];
                    rhsTok = tks[n + 1];
                }

                // normalize =< and => typos
                rel = rel.Replace("=<", "<=").Replace("=>", ">=");

                if (rel != "<=" && rel != ">=" && rel != "=")
                    throw new FormatException("Relation must be <=, >=, or =.");

                double rhsVal;
                if (!double.TryParse(rhsTok, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out rhsVal))
                    throw new FormatException("RHS is not a valid number: '" + rhsTok + "'.");

                if (rel == "=")
                    throw new NotSupportedException("Equality '=' not supported in this quick parser; convert to two inequalities or preprocess.");

                // Flip >= to <=
                if (rel == ">=")
                {
                    for (int j = 0; j < n; j++) coeffs[j] *= -1;
                    rhsVal *= -1;
                }

                constr.Add(coeffs);
                rhsList.Add(rhsVal);
            }

            Console.WriteLine($"(debug) constraints parsed: {constr.Count}");

            // Build A, b
            var A = new double[constr.Count, n];
            for (int i = 0; i < constr.Count; i++)
                for (int j = 0; j < n; j++)
                    A[i, j] = constr[i][j];
            var bArr = rhsList.ToArray();

            // Min -> Max transform
            if (isMin) { for (int j = 0; j < n; j++) c[j] *= -1; }

            var res = Solve(A, bArr, c, isInt);
            if (isMin) res.ZOpt *= -1;
            return res;
        }

        // C# 7.3-friendly helper (no tuples). Splits "<=40" / "=>-5" / "=10" into rel and rhs.
        private static void SplitRelRhsCombined_CSharp7(string token, int lineNum, out string rel, out string rhs)
        {
            token = (token ?? "").Trim();
            if (token.StartsWith("<=")) { rel = "<="; rhs = token.Substring(2); return; }
            if (token.StartsWith("=<")) { rel = "<="; rhs = token.Substring(2); return; }
            if (token.StartsWith(">=")) { rel = ">="; rhs = token.Substring(2); return; }
            if (token.StartsWith("=>")) { rel = ">="; rhs = token.Substring(2); return; }
            if (token.StartsWith("=")) { rel = "="; rhs = token.Substring(1); return; }
            throw new FormatException("Could not parse relation/RHS on line " + lineNum + ": '" + token + "'. Expected forms like '<=40' or '>=-5'.");
        }



        private static double ParseSigned(string token)
        {
            token = token.Trim();
            return double.Parse(token, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LPR_381_Project.Models;

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
        public int MaxIterations { get; set; } = 10000;
        public int MaxCuts { get; set; } = 200;

        // ------------------------------------------------------------
        // PUBLIC ENTRY: Solve from a LinearModel (preferred)
        // ------------------------------------------------------------
        public Result Solve(LinearModel model)
        {
            int n = model.Variables.Count;
            int m = model.Constraints.Count;

            // Build A (m x n) and b
            var A0 = new double[m, n];
            var b0 = new double[m];
            for (int i = 0; i < m; i++)
            {
                var coefs = model.Constraints[i].Coefficients;
                for (int j = 0; j < n; j++) A0[i, j] = coefs[j];
                b0[i] = model.Constraints[i].RHS;
            }

            // Objective c
            var c = new double[n];
            for (int j = 0; j < n; j++) c[j] = model.Variables[j].Coefficient;

            // Integer/binary mask
            var isInt = new bool[n];
            for (int j = 0; j < n; j++)
            {
                var t = model.Variables[j].Type;
                isInt[j] = (t == VarType.Integer || t == VarType.Binary);
            }

            // Minimization → negate c
            bool isMin = (model.Obj == Objective.Minimize);
            if (isMin) for (int j = 0; j < n; j++) c[j] = -c[j];

            var res = SolveInternal(A0, b0, c, isInt);

            if (isMin) res.ZOpt = -res.ZOpt;
            return res;
        }

        // ------------------------------------------------------------
        // OPTIONAL: Solve from the brief’s simple format
        //   First line: max|min c1 c2 ...
        //   Middle lines: "+a +b ... <=RHS" (>= flipped)
        //   Last line: sign row e.g. "bin bin ..." (or "int")
        // ------------------------------------------------------------
        public Result SolveFromBriefFormat(string[] rawLines)
        {
            if (rawLines == null || rawLines.Length == 0)
                throw new ArgumentException("Input is empty.");

            // Normalize
            var lines = new List<string>();
            for (int i = 0; i < rawLines.Length; i++)
            {
                string s = (rawLines[i] ?? "").Trim();
                if (s.Length == 0) continue;
                if (s.StartsWith("#") || s.StartsWith("//")) continue;
                lines.Add(s);
            }
            if (lines.Count < 3)
                throw new ArgumentException("Need at least 3 lines: objective, ≥1 constraint, sign row.");

            // Objective
            var obj = SplitTokens(lines[0]);
            bool isMax = obj[0].Equals("max", StringComparison.OrdinalIgnoreCase);
            bool isMin = obj[0].Equals("min", StringComparison.OrdinalIgnoreCase);
            if (!isMax && !isMin) throw new FormatException("Objective line must start with 'max' or 'min'.");
            int n = obj.Length - 1;
            var c = new double[n];
            for (int j = 0; j < n; j++) c[j] = ParseSigned(obj[j + 1]);

            // Sign row (last)
            var sign = SplitTokens(lines[lines.Count - 1]);
            if (sign.Length != n) throw new FormatException("Sign row must have n tokens.");
            var isInt = new bool[n];
            for (int j = 0; j < n; j++)
            {
                string tok = sign[j].ToLowerInvariant();
                isInt[j] = (tok == "bin" || tok == "int");
            }

            // Constraints
            var constr = new List<double[]>();
            var rhsList = new List<double>();
            for (int k = 1; k < lines.Count - 1; k++)
            {
                var tks = SplitTokens(lines[k]);
                if (tks.Length < n + 1)
                    throw new FormatException("Constraint must have n coeffs + relation/RHS.");

                var coeffs = new double[n];
                for (int j = 0; j < n; j++) coeffs[j] = ParseSigned(tks[j]);

                string rel, rhsTok;
                if (tks.Length == n + 1)
                {
                    SplitRelRhsCombined_CSharp7(tks[n], out rel, out rhsTok);
                }
                else
                {
                    rel = tks[n];
                    rhsTok = tks[n + 1];
                }

                rel = rel.Replace("=<", "<=").Replace("=>", ">=");

                if (rel != "<=" && rel != ">=" && rel != "=")
                    throw new FormatException("Relation must be <=, >=, or =.");

                double rhsVal;
                if (!double.TryParse(rhsTok, NumberStyles.Float, CultureInfo.InvariantCulture, out rhsVal))
                    throw new FormatException("Bad RHS: '" + rhsTok + "'.");

                if (rel == "=")
                    throw new NotSupportedException("Equality not supported in this quick parser.");

                if (rel == ">=")
                {
                    for (int j = 0; j < n; j++) coeffs[j] *= -1;
                    rhsVal *= -1;
                }

                constr.Add(coeffs);
                rhsList.Add(rhsVal);
            }

            var A = new double[constr.Count, n];
            for (int i = 0; i < constr.Count; i++)
                for (int j = 0; j < n; j++) A[i, j] = constr[i][j];
            var b = rhsList.ToArray();

            if (isMin) for (int j = 0; j < n; j++) c[j] = -c[j];

            return SolveInternal(A, b, c, isInt);
        }

        // ------------------------------------------------------------
        // CORE IMPLEMENTATION (tableau + Gomory cuts)
        // ------------------------------------------------------------
        private Result SolveInternal(double[,] A0, double[] b0, double[] c, bool[] isInteger)
        {
            // 1) Augment with x_j ≤ 1 for all integer variables (binary-type handling).
            int m0 = A0.GetLength(0);
            int n = A0.GetLength(1);

            int extraRows = 0;
            for (int j = 0; j < n; j++) if (isInteger[j]) extraRows++;

            int m = m0 + extraRows;
            var A = new double[m, n];
            var b = new double[m];

            // Copy original rows
            for (int i = 0; i < m0; i++)
            {
                for (int j = 0; j < n; j++) A[i, j] = A0[i, j];
                b[i] = b0[i];
            }
            // Add x_j ≤ 1 rows
            int r = m0;
            for (int j = 0; j < n; j++)
            {
                if (isInteger[j])
                {
                    for (int k = 0; k < n; k++) A[r, k] = 0.0;
                    A[r, j] = 1.0;
                    b[r] = 1.0;
                    r++;
                }
            }

            // 2) Build initial tableau (max form: row0 = -c)
            int cols = n + m + 1; // vars + slacks + RHS
            var T = new double[m + 1, cols];

            for (int j = 0; j < n; j++) T[0, j] = -c[j];
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < n; j++) T[i + 1, j] = A[i, j];
                T[i + 1, n + i] = 1.0;                   // slack
                T[i + 1, cols - 1] = b[i];               // RHS
            }

            var basis = new int[m];
            for (int i = 0; i < m; i++) basis[i] = n + i;

            var tableaus = new List<double[,]> { CloneMatrix(T) };
            var logs = new List<string>();

            // 3) If any RHS < 0, repair feasibility with Dual Simplex
            if (AnyNegativeRhs(T))
            {
                logs.Add("=== INITIAL DUAL SIMPLEX: fixing negative RHS ===");
                RunDualSimplex(T, basis, tableaus, logs);
            }

            // 4) Primal Simplex to solve LP relaxation
            logs.Add("=== PRIMAL SIMPLEX: LP relaxation ===");
            RunPrimalSimplex(T, basis, tableaus, logs);

            // 5) Cutting-plane loop
            int cuts = 0;
            while (true)
            {
                var x = ReadCurrentX(T, n); // original variables only
                int fractIdx = FindFractionalIntegerIndex(x, isInteger, 1e-7);
                if (fractIdx < 0)
                {
                    // Clamp tiny numeric noise on integer vars
                    for (int j = 0; j < n; j++)
                    {
                        if (isInteger[j])
                        {
                            if (Math.Abs(x[j]) < 1e-9) x[j] = 0.0;
                            if (Math.Abs(x[j] - 1.0) < 1e-9) x[j] = 1.0;
                        }
                    }
                    return new Result
                    {
                        XOpt = x,
                        ZOpt = T[0, T.GetLength(1) - 1],
                        CutsAdded = cuts,
                        Tableaus = tableaus,
                        Logs = logs
                    };
                }

                if (cuts >= MaxCuts)
                    throw new InvalidOperationException("Reached MaxCuts but solution still fractional.");

                // Try to cut from the row where that variable is basic
                int rowToCut = FindRowOfBasicVar(basis, fractIdx);
                // else look for any integer-basic row with fractional RHS
                if (rowToCut < 0) rowToCut = FindRowWithFractionalRhsOfIntegerBasic(T, basis, isInteger, n);
                // else try any row with fractional RHS
                if (rowToCut < 0) rowToCut = FindAnyRowWithFractionalRhs(T);

                if (rowToCut < 0)
                {
                    logs.Add("No fractional RHS row found; stopping.");
                    return new Result
                    {
                        XOpt = ReadCurrentX(T, n),
                        ZOpt = T[0, T.GetLength(1) - 1],
                        CutsAdded = cuts,
                        Tableaus = tableaus,
                        Logs = logs
                    };
                }

                // === ADD GOMORY CUT (keep RHS as last column!) ===
                int oldRows = T.GetLength(0);
                int oldCols = T.GetLength(1);
                double rhs = T[rowToCut, oldCols - 1];
                double bbar = FracPart(rhs);
                if (bbar <= Tolerance || 1.0 - bbar <= Tolerance)
                {
                    logs.Add("[Cut skipped] Degenerate fractional RHS on selected row.");
                    // try another row as fallback
                    int another = FindAnyRowWithFractionalRhs(T, rowToCut);
                    if (another < 0)
                    {
                        return new Result
                        {
                            XOpt = ReadCurrentX(T, n),
                            ZOpt = T[0, oldCols - 1],
                            CutsAdded = cuts,
                            Tableaus = tableaus,
                            Logs = logs
                        };
                    }
                    rowToCut = another;
                    rhs = T[rowToCut, oldCols - 1];
                    bbar = FracPart(rhs);
                }

                // Insert new slack BEFORE RHS, copy RHS to last column
                int newRows = oldRows + 1;
                int newCols = oldCols + 1;
                var Tnew = new double[newRows, newCols];

                // Copy non-RHS columns (0..oldCols-2)
                for (int i = 0; i < oldRows; i++)
                    for (int j = 0; j <= oldCols - 2; j++)
                        Tnew[i, j] = T[i, j];

                // New slack col index
                int newSlackCol = oldCols - 1;
                for (int i = 0; i < oldRows; i++) Tnew[i, newSlackCol] = 0.0;
                Tnew[0, newSlackCol] = 0.0; // objective coeff

                // Copy RHS (old last col) to new last col
                int rhsColNew = newCols - 1;
                for (int i = 0; i < oldRows; i++) Tnew[i, rhsColNew] = T[i, oldCols - 1];

                // Build cut row at index (oldRows)
                int cutRow = oldRows;
                for (int j = 0; j <= oldCols - 2; j++)
                {
                    double a = T[rowToCut, j];
                    double coeff = -FracPart(-a); // floor(a) - a
                    if (Math.Abs(coeff) < 1e-12) coeff = 0.0;
                    Tnew[cutRow, j] = coeff;
                }
                Tnew[cutRow, newSlackCol] = 1.0;
                Tnew[cutRow, rhsColNew] = -bbar; // negative RHS for dual-simplex

                // Swap in & extend basis
                T = Tnew;
                basis = basis.Concat(new[] { newSlackCol }).ToArray();

                tableaus.Add(CloneMatrix(T));
                cuts++;
                logs.Add(string.Format(CultureInfo.InvariantCulture,
                    "=== Added Gomory cut #{0} from row {1} (b̄={2:0.###}) → DUAL SIMPLEX ===",
                    cuts, rowToCut, bbar));

                // Restore feasibility
                RunDualSimplex(T, basis, tableaus, logs);
            }
        }

        // ------------------------------------------------------------
        // Simplex kernels
        // ------------------------------------------------------------
        private void RunPrimalSimplex(double[,] T, int[] basis, List<double[,]> allTableaus, List<string> logs)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            int it = 0;

            while (true)
            {
                it++;
                if (it > MaxIterations)
                    throw new InvalidOperationException("Primal Simplex: iteration limit.");

                // enter = most negative rc in row 0 (excluding RHS)
                int enter = -1; double minRc = 0.0;
                for (int j = 0; j < cols - 1; j++)
                {
                    double rc = T[0, j];
                    if (rc < minRc - Tolerance) { minRc = rc; enter = j; }
                }
                if (enter < 0)
                {
                    logs.Add("[Primal] Optimal (no negative reduced costs).");
                    break;
                }

                // leave via ratio test
                int leave = -1; double best = double.PositiveInfinity;
                for (int i = 1; i < rows; i++)
                {
                    double a = T[i, enter];
                    if (a > Tolerance)
                    {
                        double theta = T[i, cols - 1] / a;
                        if (theta < best - 1e-12) { best = theta; leave = i; }
                    }
                }
                if (leave < 0) throw new InvalidOperationException("Primal Simplex: Unbounded.");

                logs.Add(string.Format(CultureInfo.InvariantCulture,
                    "[Primal] it{0}: enter {1}, leave {2} | z={3:0.###} rc_min={4:0.###}",
                    it, enter, leave, T[0, cols - 1], minRc));

                Pivot(T, leave, enter);
                basis[leave - 1] = enter;
                allTableaus.Add(CloneMatrix(T));
            }
        }

        private void RunDualSimplex(double[,] T, int[] basis, List<double[,]> allTableaus, List<string> logs)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            int it = 0;

            while (true)
            {
                it++;
                if (it > MaxIterations)
                    throw new InvalidOperationException("Dual Simplex: iteration limit.");

                // pick leaving row: most negative RHS
                int leave = -1; double minRhs = 0.0;
                for (int i = 1; i < rows; i++)
                {
                    double rhs = T[i, cols - 1];
                    if (rhs < minRhs - Tolerance) { minRhs = rhs; leave = i; }
                }
                if (leave < 0)
                {
                    logs.Add("[Dual] Feasible (all RHS ≥ 0).");
                    break;
                }

                // entering: a<0 with minimal rc/a
                int enter = -1; double best = double.PositiveInfinity;
                for (int j = 0; j < cols - 1; j++)
                {
                    double a = T[leave, j];
                    if (a < -Tolerance)
                    {
                        double rc = T[0, j];
                        double ratio = rc / a; // a<0 ⇒ candidate
                        if (ratio < best - 1e-12) { best = ratio; enter = j; }
                    }
                }
                if (enter < 0)
                    throw new InvalidOperationException("Dual Simplex: no entering column with negative pivot row coefficient.");

                Pivot(T, leave, enter);
                basis[leave - 1] = enter;
                allTableaus.Add(CloneMatrix(T));
            }
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------
        private static bool AnyNegativeRhs(double[,] T)
        {
            int rows = T.GetLength(0), cols = T.GetLength(1);
            for (int i = 1; i < rows; i++)
                if (T[i, cols - 1] < 0) return true;
            return false;
        }

        private static int FindRowOfBasicVar(int[] basis, int varCol)
        {
            for (int i = 0; i < basis.Length; i++)
                if (basis[i] == varCol) return i + 1; // tableau row index
            return -1;
        }

        private static int FindRowWithFractionalRhsOfIntegerBasic(double[,] T, int[] basis, bool[] isInteger, int nOrig)
        {
            int rows = T.GetLength(0), cols = T.GetLength(1);
            for (int i = 1; i < rows; i++)
            {
                int col = basis[i - 1];
                if (col < nOrig && isInteger[col])
                {
                    double rhs = T[i, cols - 1];
                    if (IsFrac(rhs)) return i;
                }
            }
            return -1;
        }

        private static int FindAnyRowWithFractionalRhs(double[,] T, int skipRow = -1)
        {
            int rows = T.GetLength(0), cols = T.GetLength(1);
            for (int i = 1; i < rows; i++)
            {
                if (i == skipRow) continue;
                double rhs = T[i, cols - 1];
                if (IsFrac(rhs)) return i;
            }
            return -1;
        }

        private static double[] ReadCurrentX(double[,] T, int nOrig)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            var x = new double[nOrig];

            for (int j = 0; j < nOrig; j++)
            {
                int pivotRow = -1;
                bool unit = true;

                for (int i = 1; i < rows; i++)
                {
                    double v = T[i, j];
                    if (Math.Abs(v - 1.0) < 1e-9)
                    {
                        if (pivotRow >= 0) { unit = false; break; }
                        pivotRow = i;
                    }
                    else if (Math.Abs(v) > 1e-9)
                    {
                        unit = false; break;
                    }
                }

                if (unit && pivotRow > 0)
                    x[j] = T[pivotRow, cols - 1];
                else
                    x[j] = 0.0;
            }
            // Clean tiny noise
            for (int j = 0; j < nOrig; j++) if (Math.Abs(x[j]) < 1e-12) x[j] = 0.0;
            return x;
        }

        private static void Pivot(double[,] T, int pivotRow, int pivotCol)
        {
            int rows = T.GetLength(0);
            int cols = T.GetLength(1);
            double piv = T[pivotRow, pivotCol];
            if (Math.Abs(piv) < 1e-14) throw new InvalidOperationException("Zero pivot.");

            // scale pivot row
            for (int j = 0; j < cols; j++) T[pivotRow, j] /= piv;

            // eliminate other rows
            for (int i = 0; i < rows; i++)
            {
                if (i == pivotRow) continue;
                double f = T[i, pivotCol];
                if (Math.Abs(f) <= 1e-14) continue;
                for (int j = 0; j < cols; j++) T[i, j] -= f * T[pivotRow, j];
            }

            // zero tiny noise
            int R = T.GetLength(0), C = T.GetLength(1);
            for (int i = 0; i < R; i++)
                for (int j = 0; j < C; j++)
                    if (Math.Abs(T[i, j]) < 1e-12) T[i, j] = 0.0;
        }

        private static double[,] CloneMatrix(double[,] src)
        {
            int r = src.GetLength(0), c = src.GetLength(1);
            var dst = new double[r, c];
            Array.Copy(src, dst, src.Length);
            return dst;
        }

        private static string[] SplitTokens(string s)
        {
            return s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void SplitRelRhsCombined_CSharp7(string token, out string rel, out string rhs)
        {
            token = (token ?? "").Trim();
            if (token.StartsWith("<=")) { rel = "<="; rhs = token.Substring(2); return; }
            if (token.StartsWith("=<")) { rel = "<="; rhs = token.Substring(2); return; }
            if (token.StartsWith(">=")) { rel = ">="; rhs = token.Substring(2); return; }
            if (token.StartsWith("=>")) { rel = ">="; rhs = token.Substring(2); return; }
            if (token.StartsWith("=")) { rel = "="; rhs = token.Substring(1); return; }
            throw new FormatException("Bad relation/RHS token: '" + token + "'. Use forms like '<=40' or '>=-5'.");
        }

        private static double ParseSigned(string token)
        {
            token = token.Trim();
            return double.Parse(token, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        private static double FracPart(double x)
        {
            // Always positive fractional part in [0,1)
            double f = x - Math.Floor(x);
            if (Math.Abs(f - 1.0) < 1e-12) f = 0.0;
            return f;
        }

        private static bool IsFrac(double x)
        {
            double f = FracPart(Math.Abs(x));
            return f > 1e-7 && 1.0 - f > 1e-7;
        }

        private static int FindFractionalIntegerIndex(double[] x, bool[] isInt, double tol)
        {
            for (int j = 0; j < x.Length; j++)
            {
                if (!isInt[j]) continue;
                double f = x[j] - Math.Floor(x[j]);
                double dist = Math.Min(f, 1.0 - f);
                if (dist > tol) return j;
            }
            return -1;
        }
    }
}

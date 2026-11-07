use faer::{
    Mat, Par,
    prelude::*,
    sparse::{
        SparseColMat, Triplet,
        linalg::solvers::{Lu, SymbolicLu},
    },
};
use rayon::prelude::*;
use std::time::Instant;

type Sparse = SparseColMat<usize, f64>;

struct Case {
    matrix: Sparse,
    entries: Vec<Triplet<usize, usize, f64>>,
    rhs: Mat<f64>,
    exact: Vec<f64>,
}

impl Case {
    // Grounded resistor mesh plus one ideal voltage source. The last row
    // and column model its branch current and make the MNA matrix indefinite.
    fn mesh(side: usize, variant: usize) -> Self {
        let nodes = side * side;
        let mut entries = Vec::new();
        for row in 0..nodes {
            let conductance = 1.0 + variant as f64 / 64.0;
            let mut diagonal = 0.25;
            for neighbor in [
                (row % side > 0).then(|| row - 1),
                (row % side + 1 < side).then(|| row + 1),
                (row >= side).then(|| row - side),
                (row + side < nodes).then(|| row + side),
            ]
            .into_iter()
            .flatten()
            {
                entries.push(Triplet::new(row, neighbor, -conductance));
                diagonal += conductance;
            }
            entries.push(Triplet::new(row, row, diagonal));
        }
        entries.extend([Triplet::new(0, nodes, 1.0), Triplet::new(nodes, 0, 1.0)]);
        let exact: Vec<_> = (0..=nodes)
            .map(|i| ((i * 17 + variant * 7) % 31) as f64 / 16.0 - 1.0)
            .collect();
        let mut rhs = Mat::zeros(nodes + 1, 1);
        for t in &entries {
            rhs[(t.row, 0)] += t.val * exact[t.col];
        }
        let matrix = Sparse::try_new_from_triplets(nodes + 1, nodes + 1, &entries).unwrap();
        Self {
            matrix,
            entries,
            rhs,
            exact,
        }
    }

    fn factor(&self, symbolic: &SymbolicLu<usize>) -> Lu<usize, f64> {
        Lu::try_new_with_symbolic(symbolic.clone(), self.matrix.as_ref()).unwrap()
    }

    // Compare against the known solution and componentwise backward error.
    // A NaN or Inf is an immediate failure even in an otherwise unused row.
    fn verify(&self, result: &Mat<f64>) -> (f64, f64) {
        let n = self.exact.len();
        let mut residual: Vec<_> = (0..n).map(|i| -self.rhs[(i, 0)]).collect();
        let mut scale: Vec<_> = residual.iter().map(|v| v.abs()).collect();
        let mut error: f64 = 0.0;
        for (i, expected) in self.exact.iter().enumerate() {
            let value = result[(i, 0)];
            assert!(value.is_finite(), "nonfinite solution at row {i}");
            error = error.max((value - expected).abs());
        }
        for t in &self.entries {
            let product = t.val * result[(t.col, 0)];
            residual[t.row] += product;
            scale[t.row] += product.abs();
        }
        let backward = residual.iter().zip(scale).fold(0.0_f64, |max, (r, s)| {
            max.max(if s == 0.0 { r.abs() } else { r.abs() / s })
        });
        assert!(
            error <= 1e-10 && backward <= 1e-12,
            "solution error {error:e}, backward error {backward:e}"
        );
        (error, backward)
    }
}

fn median(samples: &mut [f64]) -> f64 {
    samples.sort_unstable_by(f64::total_cmp);
    samples[samples.len() / 2]
}

fn main() {
    println!(
        "unknowns,nnz,threads,symbolic_us,factor_us,solve_us,batch16_us,max_abs_error,max_backward_error"
    );
    for side in [8, 16, 32, 64] {
        let cases: Vec<_> = (0..16).map(|i| Case::mesh(side, i)).collect();
        let first = &cases[0];
        let mut symbolic_us = [0.0; 7];
        for t in &mut symbolic_us {
            let start = Instant::now();
            let symbolic = SymbolicLu::try_new(first.matrix.symbolic()).unwrap();
            *t = start.elapsed().as_secs_f64() * 1e6;
            std::hint::black_box(symbolic);
        }
        let symbolic = SymbolicLu::try_new(first.matrix.symbolic()).unwrap();
        for threads in [1, 2, 4, 6] {
            let pool = rayon::ThreadPoolBuilder::new()
                .num_threads(threads)
                .build()
                .unwrap();
            faer::set_global_parallelism(if threads == 1 {
                Par::Seq
            } else {
                Par::rayon(threads)
            });
            let mut factor_us = [0.0; 7];
            let mut solve_us = [0.0; 7];
            let mut max_error: f64 = 0.0;
            let mut max_backward: f64 = 0.0;
            pool.install(|| {
                let warm = first.factor(&symbolic).solve(first.rhs.as_ref());
                first.verify(&warm);
                for (factor, solve) in factor_us.iter_mut().zip(&mut solve_us) {
                    let start = Instant::now();
                    let lu = first.factor(&symbolic);
                    *factor = start.elapsed().as_secs_f64() * 1e6;
                    let start = Instant::now();
                    let result = lu.solve(first.rhs.as_ref());
                    *solve = start.elapsed().as_secs_f64() * 1e6;
                    let (error, backward) = first.verify(&result);
                    max_error = max_error.max(error);
                    max_backward = max_backward.max(backward);
                }
            });
            // Independent numerical systems share the immutable symbolic pattern.
            // Disable inner threading to avoid oversubscribing the same pool.
            faer::set_global_parallelism(Par::Seq);
            let solve_batch = || {
                cases
                    .par_iter()
                    .map(|c| c.factor(&symbolic).solve(c.rhs.as_ref()))
                    .collect::<Vec<_>>()
            };
            pool.install(solve_batch);
            let mut batch_us = [0.0; 7];
            for t in &mut batch_us {
                let start = Instant::now();
                let results = pool.install(solve_batch);
                *t = start.elapsed().as_secs_f64() * 1e6;
                for (case, result) in cases.iter().zip(results) {
                    let (error, backward) = case.verify(&result);
                    max_error = max_error.max(error);
                    max_backward = max_backward.max(backward);
                }
            }
            println!(
                "{},{},{threads},{:.3},{:.3},{:.3},{:.3},{max_error:e},{max_backward:e}",
                first.exact.len(),
                first.entries.len(),
                median(&mut symbolic_us),
                median(&mut factor_us),
                median(&mut solve_us),
                median(&mut batch_us)
            );
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn generated_cases_recover_known_solution() {
        for side in 1..=6 {
            let case = Case::mesh(side, side + 2);
            let symbolic = SymbolicLu::try_new(case.matrix.symbolic()).unwrap();
            case.verify(&case.factor(&symbolic).solve(case.rhs.as_ref()));
        }
    }

    #[test]
    #[should_panic(expected = "solution error")]
    fn verification_rejects_wrong_finite_solution() {
        let case = Case::mesh(3, 0);
        case.verify(&Mat::zeros(case.exact.len(), 1));
    }

    #[test]
    #[should_panic(expected = "nonfinite solution")]
    fn verification_rejects_nan() {
        let case = Case::mesh(3, 0);
        case.verify(&Mat::from_fn(case.exact.len(), 1, |_, _| f64::NAN));
    }
}

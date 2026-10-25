use serde::{Deserialize, Serialize};
/// Node zero is the reference; other IDs index the declared node array.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(transparent)]
pub struct NodeId(pub usize);
impl NodeId {
    pub fn unknown(self) -> Option<usize> {
        self.0.checked_sub(1)
    }
}

using UnityEngine;

namespace NeonProtocol
{
    // Remembers which blockout renderers an art pass disabled, so the pass can be reverted exactly.
    public class ArtPassRecord : MonoBehaviour
    {
        public string[] hidden = new string[0];
        public Mesh coreMesh;                 // original Core mesh/materials (restored by Revert)
        public Material[] coreMats;
        public Material cageBarMat;
    }
}

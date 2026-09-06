using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assimp;
using TpacTool.Lib;
using UkooLabs.FbxSharpie;

namespace TpacTool.IO.Assimp
{
    /// <summary>
    /// glTF 2.0 exporter via native Assimp (bypasses FbxSharpie).
    /// For Blender 5.x clients which only accept fbx/gltf importers.
    /// </summary>
    public class Gltf2Exporter : AbstractAssimpExporter
    {
        public override string AssimpFormatId => "gltf2";
        public override string Extension => "gltf";
        public override bool SupportTRSInAnimation => true;
        public override bool SupportsSecondMaterial => true;
        public override bool SupportsSecondUv => true;
        public override bool SupportsSecondColor => true;
        public override bool SupportsSkeleton => true;
        public override bool SupportsMorph => false; // glTF morph layers via Assimp produce shape-key channels that crash Blender 5.2 import; strip.
        public override bool SupportsSkeletalAnimation => true;
        public override bool SupportMorphAnimation => false;
    }
}

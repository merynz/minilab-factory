using System;

namespace ZebraDash.LevelDesign
{
    public enum SectionKind
    {
        Rest = 0,
        Active = 1,
        Drop = 2,
        Transition = 3
    }

    [Serializable]
    public sealed class SectionPlan
    {
        public SectionKind sectionType = SectionKind.Active;
        public int sectionIndex;
        public int barsLength = 4;
        public int startBar;
        public int endBarExclusive;
        public float startSec;
        public float endSec;
        public string movementProfileRef = "P1_ACTIVE";
        public float densityTarget = 0.5f;
        public float difficultyRamp = 0.5f;
    }
}

using System.Collections.Generic;

namespace CsgBrush.Editor
{
    /// <summary>Creation panel values for the selected shape. Project defaults remain in their original serialized fields.</summary>
    internal struct NewBrushOptions
    {
        public List<string> newModules;
        public int newSides;
        public int newTessellation;
        public float newStepHeight;
        public bool newHollow;
        public float newInnerRadius;
        public float newStepThickness;
        public float newCurveAngle;
        public int newStepsPer360;
        public bool newCounterClockwise;
        public bool newSlopedFloor;
        public bool newSlopedCeiling;
        public bool newSupportUnderSteps;
        public GridSnap newGridSnap;
        public float newWallThickness;
        public float newArchAngle;
        public float newDoorWidth;
        public float newDoorHeight;
        public float newWindowWidth;
        public float newWindowHeight;
        public float newWindowSill;

        public NewBrushOptions(BrushSettings defaults)
        {
            newModules = defaults.newModules;
            newSides = defaults.newSides;
            newTessellation = defaults.newTessellation;
            newStepHeight = defaults.newStepHeight;
            newHollow = defaults.newHollow;
            newInnerRadius = defaults.newInnerRadius;
            newStepThickness = defaults.newStepThickness;
            newCurveAngle = defaults.newCurveAngle;
            newStepsPer360 = defaults.newStepsPer360;
            newCounterClockwise = defaults.newCounterClockwise;
            newSlopedFloor = defaults.newSlopedFloor;
            newSlopedCeiling = defaults.newSlopedCeiling;
            newSupportUnderSteps = defaults.newSupportUnderSteps;
            newGridSnap = defaults.newGridSnap;
            newWallThickness = defaults.newWallThickness;
            newArchAngle = defaults.newArchAngle;
            newDoorWidth = defaults.newDoorWidth;
            newDoorHeight = defaults.newDoorHeight;
            newWindowWidth = defaults.newWindowWidth;
            newWindowHeight = defaults.newWindowHeight;
            newWindowSill = defaults.newWindowSill;
        }
    }
}

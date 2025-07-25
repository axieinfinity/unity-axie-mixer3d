namespace SkyMavis.AxieMixer3D
{
    public enum AxieRigType
    {
        Back_L,
        Back_M,
        Back_R,

        Ear_L,
        Ear_R,

        Eye_L,
        Eye_M,
        Eye_R,
        Eye_Accessory_L,
        Eye_Accessory_R,

        Horn_L,
        Horn_M,
        Horn_R,
        Horn_T,

        Mouth_M,
        Mouth_Accessory_L,
        Mouth_Accessory_R,

        Tail_L,
        Tail_M,
        Tail_R,
    }

    public static class AxieRigTypeExtensions
    {
        public static AxiePartType ToAxiePartType(this AxieRigType rigType) => rigType switch
        {
            AxieRigType.Back_L => AxiePartType.Back,
            AxieRigType.Back_M => AxiePartType.Back,
            AxieRigType.Back_R => AxiePartType.Back,
            AxieRigType.Ear_L => AxiePartType.Ear,
            AxieRigType.Ear_R => AxiePartType.Ear,
            AxieRigType.Eye_L => AxiePartType.Eye,
            AxieRigType.Eye_M => AxiePartType.Eye,
            AxieRigType.Eye_R => AxiePartType.Eye,
            AxieRigType.Eye_Accessory_L => AxiePartType.Eye,
            AxieRigType.Eye_Accessory_R => AxiePartType.Eye,
            AxieRigType.Horn_L => AxiePartType.Horn,
            AxieRigType.Horn_M => AxiePartType.Horn,
            AxieRigType.Horn_R => AxiePartType.Horn,
            AxieRigType.Horn_T => AxiePartType.Horn,
            AxieRigType.Mouth_M => AxiePartType.Mouth,
            AxieRigType.Mouth_Accessory_L => AxiePartType.Mouth,
            AxieRigType.Mouth_Accessory_R => AxiePartType.Mouth,
            AxieRigType.Tail_L => AxiePartType.Tail,
            AxieRigType.Tail_M => AxiePartType.Tail,
            AxieRigType.Tail_R => AxiePartType.Tail,
            _ => throw new System.ArgumentException($"Unknown AxieRigType: {rigType}"),
        };
    }
}

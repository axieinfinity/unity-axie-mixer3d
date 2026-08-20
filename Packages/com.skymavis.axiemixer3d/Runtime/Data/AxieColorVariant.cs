namespace SkyMavis.AxieMixer3D
{
    // Color-variant entry baked directly into the AxieFactory catalog (the _colors array).
    // Indexed by AxieDescriptor.colorVariant; primary1/primary2 are hex strings applied to
    // the body materials' _PrimaryColor/_SecondaryColor in AxieFactory.Colorize().
    [System.Serializable]
    internal struct AxieColorVariant
    {
        public int index;
        public string key;
        public int skin;
        public string @class;
        public int color_value;
        public string primary1;
        public string primary2;
    }
}

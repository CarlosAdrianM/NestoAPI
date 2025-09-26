namespace NestoAPI.Models.Comisiones
{
    public interface IEtiquetaComisionAcumulada : IEtiquetaComisionVenta
    {
        // Propiedades específicas para etiquetas con comisión acumulada
        decimal FaltaParaSalto { get; set; }
        decimal InicioTramo { get; set; }
        decimal FinalTramo { get; set; }
        bool BajaSaltoMesSiguiente { get; set; }
        decimal Proyeccion { get; set; }
        decimal VentaAcumulada { get; set; }
        decimal ComisionAcumulada { get; set; }
        decimal TipoConseguido { get; set; }
        decimal TipoReal { get; }

        // Propiedades de estrategia de sobrepago
        string EstrategiaUtilizada { get; set; }
        decimal? TipoCorrespondePorTramo { get; set; }
        decimal? TipoRealmenteAplicado { get; set; }
        string MotivoEstrategia { get; set; }
        decimal? ComisionSinEstrategia { get; set; }

        // Propiedades calculadas
        bool TieneEstrategiaEspecial { get; }
        bool EsSobrepago { get; }
        decimal ComisionRecuperadaEsteMes { get; }
        string TextoSobrepago { get; }
    }
}

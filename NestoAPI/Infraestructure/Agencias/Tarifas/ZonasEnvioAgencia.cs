namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Zonas de envío para el cálculo de tarifas de agencia. Portado de Nesto
    /// (Nesto.ViewModels.Agencias.ZonasEnvioAgencia) al mover el comparador a NestoAPI.
    /// </summary>
    public enum ZonasEnvioAgencia
    {
        Provincial,
        Peninsular,
        BalearesMayores,
        BalearesMenores,
        CanariasMayores,
        CanariasMenores,
        Portugal,
        Extranjero
    }
}

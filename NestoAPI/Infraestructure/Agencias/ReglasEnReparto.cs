using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#516: un envío tramitado está «en reparto» (sale hoy para entregar) según el último texto de la
    /// agencia que guarda el poll de seguimiento en DetalleEstado. No es un estado: el envío sigue en Tramitados.
    /// Textos vistos en producción (30 días, 24/09/26): GLS «EN REPARTO», CTT «EN REPARTO» (evento 1500),
    /// Innovatrans «REPARTO». Los demás (REPARTO FALLIDO, NUEVO REPARTO…) no cuentan.
    /// </summary>
    public static class ReglasEnReparto
    {
        public static bool EstaEnReparto(short estado, string detalleEstado)
        {
            if (estado != Constantes.Agencias.ESTADO_TRAMITADO || string.IsNullOrWhiteSpace(detalleEstado))
            {
                return false;
            }
            string texto = detalleEstado.Trim().ToUpperInvariant();
            return texto == "EN REPARTO" || texto == "REPARTO";
        }
    }
}

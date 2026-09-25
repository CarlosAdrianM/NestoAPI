using NestoAPI.Models.Cobros;
using System;

namespace NestoAPI.Infraestructure.Cobros
{
    /// <summary>
    /// NestoAPI#544 (c): cada cuánto se reclama (decisión de Carlos, 25/09/26), contando desde el
    /// aviso anterior: 1.º al cumplir el umbral de días (eso lo decide el selector), 2.º a los 10
    /// días, 3.º a los 5, 4.º a los 2 y desde ahí diario (laborables: el job corre de lunes a
    /// viernes). Nunca dos el mismo día. El reloj se reinicia si el cliente paga parte (el
    /// pendiente de hoy es menor que el del último aviso); si administración liquida el efecto,
    /// este deja de estar pendiente y desaparece del criterio.
    /// </summary>
    public static class CadenciaAvisosFacturasVencidas
    {
        /// <summary>Días que tienen que pasar DESPUÉS del aviso número <paramref name="numeroAviso"/> para mandar el siguiente.</summary>
        public static int DiasHastaElSiguiente(int numeroAviso)
        {
            switch (numeroAviso)
            {
                case 1: return 10;
                case 2: return 5;
                case 3: return 2;
                default: return numeroAviso < 1 ? 0 : 1;
            }
        }

        /// <summary>
        /// Rellena en el efecto su memoria (número de aviso que sería hoy, fecha en que toca, si
        /// toca hoy). <paramref name="ultimo"/> es el último aviso registrado del efecto o null.
        /// </summary>
        public static void Aplicar(AvisoFacturaVencidaDTO efecto, AvisoFacturaVencidaRegistrado ultimo, DateTime hoy)
        {
            if (efecto == null)
            {
                throw new ArgumentNullException(nameof(efecto));
            }
            hoy = hoy.Date;
            if (ultimo == null)
            {
                efecto.NumeroUltimoAviso = 0;
                efecto.FechaUltimoAviso = null;
                efecto.NumeroAviso = 1;
                efecto.FechaSiguienteAviso = null;
                efecto.TocaHoy = true;
                efecto.ReinicioPorPagoParcial = false;
                return;
            }

            efecto.NumeroUltimoAviso = ultimo.NumeroAviso;
            efecto.FechaUltimoAviso = ultimo.Fecha.Date;

            if (efecto.Importe < ultimo.ImportePendiente)
            {
                // Ha pagado parte desde el último aviso: la cuenta empieza de nuevo, pero nunca dos
                // avisos el mismo día.
                efecto.ReinicioPorPagoParcial = true;
                efecto.NumeroAviso = 1;
                efecto.FechaSiguienteAviso = ultimo.Fecha.Date.AddDays(1);
                efecto.TocaHoy = hoy > ultimo.Fecha.Date;
                return;
            }

            efecto.ReinicioPorPagoParcial = false;
            efecto.NumeroAviso = ultimo.NumeroAviso + 1;
            efecto.FechaSiguienteAviso = ultimo.Fecha.Date.AddDays(DiasHastaElSiguiente(ultimo.NumeroAviso));
            efecto.TocaHoy = hoy >= efecto.FechaSiguienteAviso.Value;
        }

        /// <summary>Texto para la sombra y el resumen: «1.er aviso», «2.º aviso»...</summary>
        public static string Ordinal(int numeroAviso)
        {
            switch (numeroAviso)
            {
                case 1: return "1.er aviso";
                case 3: return "3.er aviso";
                default: return numeroAviso + ".º aviso";
            }
        }
    }
}

using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#780: la puerta de los festivos de los jobs recurrentes de lunes a viernes. El cron de Hangfire solo sabe de días
    /// de la semana, así que un festivo de Algete (el 02/11/2026, p. ej.) cae como un día más: los jobs que avisan a personas o
    /// actúan sobre el almacén tienen que preguntarlo ellos. Una sola implementación para todos (DRY).
    /// </summary>
    public static class JobsFestivos
    {
        /// <summary>El calendario de la oficina y el almacén (Algete), el mismo que usan la remesa y los recordatorios.</summary>
        public static bool EsFestivoAlgete(DateTime dia) => GestorFestivos.EsFestivo(dia, Constantes.Almacenes.ALGETE);

        /// <summary>
        /// True si hoy (fecha local de <paramref name="ahora"/>) es festivo y el job no debe hacer nada: deja la traza
        /// informativa <paramref name="mensaje"/> en ELMAH con <paramref name="usuarioSistema"/>. Si no se puede saber (falla
        /// el calendario o faltan las funciones), el job sigue como siempre: antes trabajar de más que dejar algo sin hacer.
        /// </summary>
        public static bool SaltarSiFestivo(string mensaje, string usuarioSistema, Func<DateTime, bool> esFestivo, Func<DateTime> ahora,
            Action<Exception, string> trazar = null)
        {
            Action<Exception, string> traza = trazar ?? ElmahHelper.Log;
            if (esFestivo == null || ahora == null)
            {
                return false;
            }
            try
            {
                if (!esFestivo(ahora().Date))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                traza(new Exception($"[Festivos #780] No se ha podido comprobar si hoy es festivo y el job sigue como en un laborable ({ex.Message}). En festivo habría sido: {mensaje}", ex), usuarioSistema);
                return false;
            }
            traza(new Exception(mensaje), usuarioSistema);
            return true;
        }
    }
}

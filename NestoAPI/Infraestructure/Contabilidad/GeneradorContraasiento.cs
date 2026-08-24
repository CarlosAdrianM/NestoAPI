using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Contabilidad
{
    /// <summary>
    /// NestoAPI#397: convierte apuntes de <see cref="Models.Contabilidad"/> en las líneas de
    /// <see cref="PreContabilidad"/> de su CONTRAASIENTO.
    ///
    /// Un contraasiento es el mismo asiento con el Debe y el Haber cambiados: anula al original
    /// sin borrar nada, y la suma de los dos da cero. Todo lo demás se copia TAL CUAL, y eso no es
    /// un capricho: si el contraasiento no cuadra campo a campo con el original, deja de anularlo
    /// en cuanto un informe agrupe por delegación, vendedor, ruta, forma de venta o cualquier otra
    /// de esas dimensiones. El objetivo es que se anulen entre sí MIREN COMO MIREN.
    ///
    /// Es una clase pura a propósito (entra una lista, sale otra): el mapeo campo a campo es lo
    /// único que de verdad hay que cubrir con tests, y así se cubre sin base de datos.
    /// </summary>
    public static class GeneradorContraasiento
    {
        internal const string PREFIJO_CONCEPTO = "Contraasiento ";

        /// <summary>Límite de la columna Concepto en PreContabilidad.</summary>
        internal const int LONGITUD_MAXIMA_CONCEPTO = 50;

        /// <summary>
        /// Agrupa los apuntes por su asiento de origen. Decisión de Carlos (24/08/26): si se
        /// seleccionan apuntes de asientos distintos, cada asiento original queda anulado por SU
        /// propio contraasiento; mezclarlo todo en uno solo juntaría reversiones de operaciones
        /// que no tenían nada que ver.
        /// </summary>
        public static IEnumerable<IGrouping<int?, Models.Contabilidad>> AgruparPorAsiento(
            IEnumerable<Models.Contabilidad> apuntes)
        {
            return (apuntes ?? Enumerable.Empty<Models.Contabilidad>())
                .GroupBy(a => a.Asiento)
                .OrderBy(g => g.Key);
        }

        /// <summary>
        /// Las líneas de PreContabilidad del contraasiento de un grupo de apuntes.
        /// </summary>
        /// <param name="apuntes">Apuntes originales (normalmente los de un mismo asiento).</param>
        /// <param name="diario">Diario donde se dejan las líneas para contabilizar.</param>
        /// <param name="usuario">Usuario del Identity, para la auditoría.</param>
        /// <param name="fecha">
        /// Fecha del contraasiento. Se pasa desde fuera y no se deduce aquí porque puede ser la del
        /// original o, si su mes ya está cerrado, la que decida el usuario (ver el servicio).
        /// </param>
        public static List<PreContabilidad> Generar(
            IEnumerable<Models.Contabilidad> apuntes, string diario, string usuario, DateTime fecha)
        {
            return (apuntes ?? Enumerable.Empty<Models.Contabilidad>())
                .Select(a => GenerarLinea(a, diario, usuario, fecha))
                .ToList();
        }

        internal static PreContabilidad GenerarLinea(
            Models.Contabilidad apunte, string diario, string usuario, DateTime fecha)
        {
            if (apunte == null)
            {
                throw new ArgumentNullException(nameof(apunte));
            }

            return new PreContabilidad
            {
                // ===== Lo ÚNICO que cambia =====
                Debe = apunte.Haber,
                Haber = apunte.Debe,
                Concepto = ConceptoContraasiento(apunte.Concepto),

                // ===== Copiado tal cual =====
                Empresa = apunte.Empresa,
                TipoApunte = apunte.TipoApunte,
                Nº_Cuenta = apunte.Nº_Cuenta,
                Nº_Documento = apunte.Nº_Documento,
                Delegación = apunte.Delegación,
                FormaVenta = apunte.FormaVenta,
                CentroCoste = apunte.CentroCoste,
                Departamento = apunte.Departamento,
                Origen = apunte.Origen,

                Fecha = fecha,
                Diario = diario,

                // ===== Auditoria: de QUIEN hace el contraasiento, no del apunte original =====
                // El original conserva su Usuario y su Fecha_Modificacion; el contraasiento es un
                // apunte nuevo y responde de quien lo crea y de cuando.
                Usuario = usuario,
                Fecha_Modificación = DateTime.Now,

                // Asiento_Automático lo asigna prdContabilizar; aquí se deja sin número de asiento.
                Asiento = null,

                // ===== Liquidado: SIEMPRE vacío (decisión de Carlos, 24/08/26) =====
                // Si el apunte original liquidaba un efecto de cliente, copiar ese Liquidado haría
                // que prdContabilizar llamase a prdLiquidar OTRA VEZ sobre el mismo efecto (ver
                // #296/#311). El contraasiento revierte el importe y nada más: deshacer una
                // liquidación es otra operación y se hace a conciencia, no de rebote.
                Liquidado = null
            };
        }

        /// <summary>
        /// Antepone "Contraasiento " al concepto original. Si no cabe en la columna se recorta por
        /// el FINAL: el prefijo es lo que hace entendible el apunte de un vistazo en el mayor, así
        /// que es lo último que se puede perder.
        /// </summary>
        internal static string ConceptoContraasiento(string conceptoOriginal)
        {
            string concepto = PREFIJO_CONCEPTO + (conceptoOriginal ?? string.Empty).Trim();

            return concepto.Length <= LONGITUD_MAXIMA_CONCEPTO
                ? concepto
                : concepto.Substring(0, LONGITUD_MAXIMA_CONCEPTO);
        }
    }
}

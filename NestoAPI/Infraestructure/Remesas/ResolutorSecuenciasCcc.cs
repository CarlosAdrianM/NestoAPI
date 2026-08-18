using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Remesas
{
    /// <summary>
    /// NestoAPI#368: el SP prdCrearRemesaIso20022 aborta si CUALQUIER cliente de la BD (esté o
    /// no en la remesa) tiene la Secuencia (FRST/RCUR) distinta entre contactos que comparten
    /// el mismo número de CCC — el chequeo del SP es global y bloqueaba TODAS las remesas sin
    /// mensaje para el usuario. Esta clase decide, para cada grupo incoherente, si se puede
    /// autocurar o hay que parar con un error accionable:
    /// - FRST vs RCUR con la MISMA cuenta bancaria → unificar a RCUR (el mandato es único por
    ///   cuenta para todos los contactos —comentario del propio SP— y si alguno es RCUR es que
    ///   ya hubo un cobro del mandato; pasar de FRST a RCUR es seguro para el banco).
    /// - Cuentas bancarias DISTINTAS bajo el mismo número de CCC, o secuencias no reconocidas
    ///   → error con el detalle exacto (no se unifica a ciegas lo que no es el mismo mandato).
    /// </summary>
    internal static class ResolutorSecuenciasCcc
    {
        internal const string FRST = "FRST";
        internal const string RCUR = "RCUR";
        internal const string TEXTO_ERROR_SP = "tienen secuencias diferentes";

        /// <summary>Detecta el raiserror del SP (red de seguridad si el pre-chequeo no corrió).</summary>
        internal static bool EsErrorDeSecuencias(string mensaje)
        {
            return mensaje != null
                && mensaje.IndexOf(TEXTO_ERROR_SP, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static ResolucionSecuencias Resolver(IEnumerable<CccSecuencia> filasIncoherentes)
        {
            var resolucion = new ResolucionSecuencias();
            if (filasIncoherentes == null)
            {
                return resolucion;
            }

            var grupos = filasIncoherentes.GroupBy(f => new { Empresa = f.Empresa?.Trim(), Cliente = f.Cliente?.Trim(), Ccc = f.Ccc?.Trim() });

            foreach (var grupo in grupos)
            {
                var filas = grupo.OrderBy(f => f.Contacto?.Trim()).ToList();
                var secuencias = filas.Select(f => f.Secuencia?.Trim() ?? string.Empty).Distinct().ToList();
                if (secuencias.Count <= 1)
                {
                    continue; // coherente (o solo difería en espacios): nada que hacer
                }

                string detalle = string.Join(", ", filas.Select(f => $"contacto {f.Contacto?.Trim()}: {f.Secuencia?.Trim()}"));
                var cuentas = filas.Select(f => f.CuentaBancaria?.Trim() ?? string.Empty).Distinct().ToList();

                if (cuentas.Count > 1)
                {
                    resolucion.Errores.Add(
                        $"El cliente {grupo.Key.Cliente} tiene cuentas bancarias distintas entre contactos " +
                        $"con el mismo número de CCC ({grupo.Key.Ccc}) y secuencias diferentes ({detalle}). " +
                        "Revisa sus fichas CCC: no son el mismo mandato y no se pueden unificar automáticamente.");
                }
                else if (secuencias.All(s => s == FRST || s == RCUR))
                {
                    resolucion.UnificarARcur.Add(new GrupoCccUnificar
                    {
                        Empresa = grupo.Key.Empresa,
                        Cliente = grupo.Key.Cliente,
                        Ccc = grupo.Key.Ccc,
                        Detalle = detalle
                    });
                }
                else
                {
                    resolucion.Errores.Add(
                        $"El cliente {grupo.Key.Cliente} tiene secuencias no reconocidas en el CCC " +
                        $"{grupo.Key.Ccc} ({detalle}). Corrige la Secuencia (FRST/RCUR) en la ficha CCC.");
                }
            }
            return resolucion;
        }
    }

    /// <summary>Proyección de una fila de la tabla ccc con lo necesario para decidir.</summary>
    internal class CccSecuencia
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Ccc { get; set; }
        public string Contacto { get; set; }
        public string Secuencia { get; set; }
        public string CuentaBancaria { get; set; }
    }

    internal class GrupoCccUnificar
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Ccc { get; set; }
        public string Detalle { get; set; }
    }

    internal class ResolucionSecuencias
    {
        public List<GrupoCccUnificar> UnificarARcur { get; } = new List<GrupoCccUnificar>();
        public List<string> Errores { get; } = new List<string>();
    }
}

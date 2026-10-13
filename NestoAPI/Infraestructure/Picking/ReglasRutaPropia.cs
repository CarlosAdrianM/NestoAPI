using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Picking
{
    /// <summary>
    /// Sugerencia 575 (Alfredo, almacén; aprobada por Carlos el 13/10/26): un pedido que va por OnTime (FW) pero que
    /// probablemente acabará saliendo por NUESTRA ruta (AT «Atención técnica» o 16 «Ruta diaria»), con la ruta que se
    /// propone y por qué. Solo se enseña a almacén al sacar el picking; quien mete el pedido no lo ve nunca.
    /// </summary>
    public class SugerenciaRutaPedidoDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime? FechaPedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public decimal BaseImponible { get; set; }
        public string RutaActual { get; set; }
        public string RutaPropuesta { get; set; }
        /// <summary>Por qué se propone (puede haber varios motivos a la vez), en lenguaje de almacén.</summary>
        public List<string> Motivos { get; set; } = new List<string>();
    }

    /// <summary>Sugerencia 575: lo que se lee de cada pedido candidato para decidir si se sugiere ruta propia.</summary>
    public class DatosPedidoSugerenciaRuta
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime? FechaPedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        /// <summary>Ruta que lleva ahora el pedido.</summary>
        public string Ruta { get; set; }
        /// <summary>Base imponible de las líneas de producto que entrarían en el picking.</summary>
        public decimal BaseImponible { get; set; }
        /// <summary>Alguna de esas líneas es de aparatos (grupo APA).</summary>
        public bool TieneAparatos { get; set; }
        /// <summary>Ruta con la que salió el último pedido servido del cliente (null si no hay).</summary>
        public string RutaUltimoPedido { get; set; }
    }

    /// <summary>
    /// Sugerencia 575: las reglas, sin base de datos. Salen del estudio de 6 meses (abr-oct/26) de los pedidos de
    /// clientes con ruta FW en la ficha que acabaron saliendo por AT o 16 (Alfredo cambia la ruta a mano, normalmente
    /// DESPUÉS del picking, y por eso NestoAPI#588 no le servía):
    /// <list type="bullet">
    /// <item>Empresa 3 (todo contado) y código postal de Madrid (28…): el 77 % de sus pedidos acaba por AT/16.</item>
    /// <item>El último pedido del cliente salió por AT o 16 (cualquier empresa).</item>
    /// <item>Empresa 1 con aparatos (grupo APA) y 1.500 € o más de base: por AT.</item>
    /// </list>
    /// Es una pista para almacén: decide Alfredo, nunca se cambia sola.
    /// </summary>
    public static class ReglasRutaPropia
    {
        public const string RUTA_ATENCION_TECNICA = "AT";
        public const string RUTA_DIARIA = "16";
        public const string GRUPO_APARATOS = "APA";
        public const decimal IMPORTE_MINIMO_APARATOS = 1500M;

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Rutas de OnTime: las únicas en las que se sugiere pasar a nuestra ruta (00 es fuera de Madrid).</summary>
        public static readonly IReadOnlyList<string> RUTAS_AGENCIA_MADRID = new List<string> { "FW", "OT" }.AsReadOnly();

        /// <summary>Las rutas propias, las mismas que la facturación de rutas (<see cref="Models.Facturas.RutaPropia"/>).</summary>
        public static readonly IReadOnlyList<string> RUTAS_PROPIAS = new Models.Facturas.RutaPropia().RutasContenidas;

        /// <summary>Zonas (3 primeras cifras del código postal) que se sirven por AT; el resto, por la 16 (≈80 % de acierto).</summary>
        public static readonly IReadOnlyList<string> ZONAS_ATENCION_TECNICA = new List<string> { "288", "289", "283" }.AsReadOnly();

        public static bool EsRutaPropia(string ruta)
        {
            return !string.IsNullOrWhiteSpace(ruta) && RUTAS_PROPIAS.Contains(ruta.Trim().ToUpperInvariant());
        }

        public static bool EsRutaAgenciaMadrid(string ruta)
        {
            return !string.IsNullOrWhiteSpace(ruta) && RUTAS_AGENCIA_MADRID.Contains(ruta.Trim().ToUpperInvariant());
        }

        public static bool EsCodigoPostalDeMadrid(string codigoPostal)
        {
            return !string.IsNullOrWhiteSpace(codigoPostal) && codigoPostal.Trim().StartsWith("28", StringComparison.Ordinal);
        }

        /// <summary>AT para las zonas 288, 289 y 283; la 16 para el resto.</summary>
        public static string RutaPorZona(string codigoPostal)
        {
            string cp = codigoPostal?.Trim() ?? string.Empty;
            return cp.Length >= 3 && ZONAS_ATENCION_TECNICA.Contains(cp.Substring(0, 3)) ? RUTA_ATENCION_TECNICA : RUTA_DIARIA;
        }

        /// <summary>
        /// La sugerencia para un pedido, o null si no hay nada que sugerir (ya va por nuestra ruta, no va por OnTime o
        /// no cumple ninguna regla). Ruta propuesta: aparatos ≥ 1.500 € → AT; si no, la del último pedido si fue por
        /// AT/16 (en los datos acierta igual o más que la zona: 95 de 115 en empresa 3 y 12 de 12 en empresa 1, frente a
        /// 93 y 5 por zona); si no, por zona.
        /// </summary>
        public static SugerenciaRutaPedidoDTO Sugerir(DatosPedidoSugerenciaRuta datos)
        {
            if (datos == null || EsRutaPropia(datos.Ruta) || !EsRutaAgenciaMadrid(datos.Ruta))
            {
                return null;
            }

            string empresa = datos.Empresa?.Trim();
            var motivos = new List<string>();
            string propuesta = null;

            if (empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && datos.TieneAparatos && datos.BaseImponible >= IMPORTE_MINIMO_APARATOS)
            {
                motivos.Add(string.Format(ES, "Lleva aparatos y {0:#,##0.00} € de base (1.500 € o más)", datos.BaseImponible));
                propuesta = RUTA_ATENCION_TECNICA;
            }

            if (EsRutaPropia(datos.RutaUltimoPedido))
            {
                string rutaUltimo = datos.RutaUltimoPedido.Trim().ToUpperInvariant();
                motivos.Add($"Su último pedido salió por la {rutaUltimo}");
                propuesta = propuesta ?? rutaUltimo;
            }

            if (empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO && EsCodigoPostalDeMadrid(datos.CodigoPostal))
            {
                motivos.Add("Contado (empresa 3) con código postal de Madrid");
                propuesta = propuesta ?? RutaPorZona(datos.CodigoPostal);
            }

            if (!motivos.Any())
            {
                return null;
            }

            return new SugerenciaRutaPedidoDTO
            {
                Empresa = empresa,
                Pedido = datos.Pedido,
                FechaPedido = datos.FechaPedido,
                Cliente = datos.Cliente?.Trim(),
                Contacto = datos.Contacto?.Trim(),
                Nombre = datos.Nombre?.Trim(),
                CodigoPostal = datos.CodigoPostal?.Trim(),
                Poblacion = datos.Poblacion?.Trim(),
                BaseImponible = datos.BaseImponible,
                RutaActual = datos.Ruta?.Trim(),
                RutaPropuesta = propuesta,
                Motivos = motivos
            };
        }
    }
}

using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana
{
    /// <summary>Cuerpo de <c>POST api/PedidosVenta/AvisoCondicionesCampana</c>: lo que lleva el pedido que se va a crear.</summary>
    public class AvisoCondicionesCampanaPeticion
    {
        public string Empresa { get; set; }
        /// <summary>Referencias de las líneas del pedido (se repiten o no, da igual).</summary>
        public List<string> Productos { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        /// <summary>Opcional: el pedido ya lleva un prepago (tarjeta, Aplázame, transferencia previa…).</summary>
        public bool TienePrepago { get; set; }
        /// <summary>
        /// Opcional (Carlos 11/11/26): el descuento por pronto pago que lleva el pedido (0,03 = 3 %). Mayor que 0 = pronto pago =
        /// ya es contado. Sin el campo vale 0 y decide solo con los plazos (que con PP en la tabla PlazosPago también cuentan).
        /// </summary>
        public decimal DescuentoPP { get; set; }
    }

    /// <summary>Respuesta de <c>POST api/PedidosVenta/AvisoCondicionesCampana</c>.</summary>
    public class AvisoCondicionesCampanaRespuesta
    {
        /// <summary>True: hay que preguntar al usuario con <see cref="Mensaje"/> (Sí → poner <see cref="PlazosPagoPropuestos"/>).</summary>
        public bool Avisar { get; set; }
        public string Mensaje { get; set; }
        public string PlazosPagoPropuestos { get; set; }
        /// <summary>La campaña (o campañas, separadas por comas) que dispara el aviso; null si no se avisa.</summary>
        public string Campana { get; set; }
        /// <summary>Los productos del pedido que están en una campaña vigente con condición de contado.</summary>
        public List<string> Productos { get; set; } = new List<string>();
    }

    /// <summary>
    /// Feria online 12-16/11/26 (Carlos 11/11/26): las ofertas de la feria exigen pago al contado, pero no se bloquea nada; solo
    /// se informa al CREAR el pedido (también si es un presupuesto). Núcleo puro: decide con las filas de
    /// <see cref="CampanasCondicionContado"/> y el día que se le pase.
    /// <para>«Ya está al contado» (no se avisa): plazos CONTADO, CR (contado riguroso) o PRE (prepago: así entran Aplázame y los
    /// pagos con tarjeta de la tienda y la app), forma de pago TAR (tarjeta), <see cref="AvisoCondicionesCampanaPeticion.TienePrepago"/>
    /// o pronto pago (Carlos 11/11/26: «al fin y al cabo son al contado»): <see cref="AvisoCondicionesCampanaPeticion.DescuentoPP"/>
    /// mayor que 0, o unos plazos con descuento por pronto pago en la tabla PlazosPago (<see cref="PlazosProntoPago"/>: PP, PP2, PP5…).</para>
    /// </summary>
    public static class AvisoCondicionesCampana
    {
        public const string MENSAJE_POR_DEFECTO =
            "Este pedido lleva productos de la feria online, con condiciones de la feria online, y debería ser al contado para poder llevar esos precios. ¿Cambiar a contado?";

        private static readonly string[] PLAZOS_YA_CONTADO =
        {
            Constantes.PlazosPago.CONTADO,
            Constantes.PlazosPago.CONTADO_RIGUROSO,
            Constantes.PlazosPago.PREPAGO
        };

        public static bool YaEsContado(string formaPago, string plazosPago, bool tienePrepago)
        {
            if (tienePrepago)
            {
                return true;
            }
            string plazos = plazosPago?.Trim();
            if (!string.IsNullOrEmpty(plazos) && PLAZOS_YA_CONTADO.Contains(plazos, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
            return string.Equals(formaPago?.Trim(), Constantes.FormasPago.TARJETA, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Vigencia por día inclusive.</summary>
        internal static bool EsVigente(ProductoCampanaContado fila, DateTime hoy) =>
            fila.FechaDesde.Date <= hoy.Date && hoy.Date <= fila.FechaHasta.Date;

        /// <summary>Pronto pago: descuento PP puesto en el pedido o plazos que lo llevan en la tabla PlazosPago.</summary>
        public static bool EsProntoPago(AvisoCondicionesCampanaPeticion peticion, IEnumerable<PlazoProntoPago> plazosConProntoPago)
        {
            return peticion != null && (peticion.DescuentoPP > 0 ||
                PlazosProntoPago.LlevanProntoPago(plazosConProntoPago, peticion.Empresa, peticion.PlazosPago));
        }

        public static AvisoCondicionesCampanaRespuesta Decidir(IEnumerable<ProductoCampanaContado> filas, DateTime hoy,
            AvisoCondicionesCampanaPeticion peticion, IEnumerable<PlazoProntoPago> plazosConProntoPago = null)
        {
            var noAvisar = new AvisoCondicionesCampanaRespuesta { Avisar = false };
            if (peticion?.Productos == null || peticion.Productos.Count == 0)
            {
                return noAvisar;
            }
            if (YaEsContado(peticion.FormaPago, peticion.PlazosPago, peticion.TienePrepago) || EsProntoPago(peticion, plazosConProntoPago))
            {
                return noAvisar;
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            List<ProductoCampanaContado> vigentes = (filas ?? Enumerable.Empty<ProductoCampanaContado>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto) && EsVigente(f, hoy) &&
                            string.Equals(string.IsNullOrWhiteSpace(f.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : f.Empresa.Trim(),
                                empresa, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (vigentes.Count == 0)
            {
                return noAvisar;
            }

            var porProducto = new Dictionary<string, ProductoCampanaContado>(StringComparer.OrdinalIgnoreCase);
            foreach (ProductoCampanaContado fila in vigentes)
            {
                string producto = fila.Producto.Trim();
                if (!porProducto.ContainsKey(producto))
                {
                    porProducto.Add(producto, fila);
                }
            }

            var disparan = new List<string>();
            var campanas = new List<ProductoCampanaContado>();
            foreach (string referencia in peticion.Productos)
            {
                string producto = referencia?.Trim();
                if (string.IsNullOrEmpty(producto) || disparan.Contains(producto, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (porProducto.TryGetValue(producto, out ProductoCampanaContado fila))
                {
                    disparan.Add(producto);
                    if (!campanas.Any(c => string.Equals(c.Campana?.Trim(), fila.Campana?.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        campanas.Add(fila);
                    }
                }
            }
            if (disparan.Count == 0)
            {
                return noAvisar;
            }

            string texto = campanas.Select(c => c.TextoAviso?.Trim()).FirstOrDefault(t => !string.IsNullOrEmpty(t));
            return new AvisoCondicionesCampanaRespuesta
            {
                Avisar = true,
                Mensaje = texto ?? MENSAJE_POR_DEFECTO,
                PlazosPagoPropuestos = Constantes.PlazosPago.CONTADO,
                Campana = string.Join(", ", campanas.Select(c => c.Campana?.Trim()).Where(c => !string.IsNullOrEmpty(c))),
                Productos = disparan
            };
        }
    }
}

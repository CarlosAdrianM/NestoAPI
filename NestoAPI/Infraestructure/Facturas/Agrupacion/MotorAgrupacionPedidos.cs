using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 2): implementación del motor de agrupación.
    ///
    /// El "movimiento" de una línea de un pedido a otro es, en este modelo de datos,
    /// simplemente cambiar <see cref="LinPedidoVta.Número"/> (es lo que hace el SP
    /// <c>prdAgruparAlbaranesVta</c> a través de la vista actualizable
    /// <c>vstAgruparAlbaranesVta</c>). Cada línea conserva su propio <c>Contacto</c>
    /// (dirección de entrega), de modo que la factura resultante puede contener líneas
    /// de varios contactos: <c>prdCrearFacturaVta</c> ya lo soporta (el deudor 430 sale
    /// del contacto de cabecera y el asiento de ventas se agrupa por cuenta, no por
    /// contacto), por lo que NO hay que tocar el SP de facturación.
    ///
    /// Al mover las líneas hay que repuntar las tablas satélite que referencian la línea
    /// por su <c>Nº_Orden</c> (identidad global): PedidosEspeciales y Ubicaciones, igual
    /// que hace el SP de FDM.
    /// </summary>
    public class MotorAgrupacionPedidos : IMotorAgrupacionPedidos
    {
        private readonly NVEntities db;

        public MotorAgrupacionPedidos(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public CabPedidoVta Agrupar(IEnumerable<CabPedidoVta> origenes, CabPedidoVta destino)
        {
            return Agrupar(origenes, destino, null);
        }

        /// <summary>
        /// NestoAPI#727: ¿se mueve esta línea al agrupar? Las que están en albarán y, si hay fecha tope (fin de mes), cuya
        /// fecha de albarán es igual o anterior a ese día (una línea en albarán sin fecha de albarán no entra en un corte).
        /// La usan el motor y el fin de mes para contar lo que se mueve, con el mismo criterio.
        /// </summary>
        public static bool LineaAgrupable(LinPedidoVta linea, DateTime? fechaAlbaranHasta)
        {
            if (linea == null || linea.Estado != Constantes.EstadosLineaVenta.ALBARAN)
            {
                return false;
            }
            if (!fechaAlbaranHasta.HasValue)
            {
                return true;
            }
            return linea.Fecha_Albarán.HasValue && linea.Fecha_Albarán.Value < fechaAlbaranHasta.Value.Date.AddDays(1);
        }

        public CabPedidoVta Agrupar(IEnumerable<CabPedidoVta> origenes, CabPedidoVta destino, DateTime? fechaAlbaranHasta)
        {
            if (destino == null)
            {
                throw new ArgumentNullException(nameof(destino));
            }
            if (origenes == null)
            {
                throw new ArgumentNullException(nameof(origenes));
            }

            // Líneas que de verdad hay que mover: las de los pedidos origen distintos del
            // destino y que están en albarán (Estado == ALBARAN). Las líneas ya facturadas
            // (Estado FACTURA) no se tocan, y las pendientes no deberían llegar aquí porque
            // el gate de facturación (Fase 1) impide agrupar si algún hermano no está listo.
            // NestoAPI#727: en el fin de mes, además, solo las de fecha de albarán hasta el corte (las demás se quedan).
            List<LinPedidoVta> lineasAMover = origenes
                .Where(o => o != null && o.Número != destino.Número)
                .SelectMany(o => (o.LinPedidoVtas ?? new List<LinPedidoVta>())
                    .Where(l => LineaAgrupable(l, fechaAlbaranHasta)))
                .ToList();

            foreach (LinPedidoVta linea in lineasAMover)
            {
                CabPedidoVta origen = linea.CabPedidoVta;

                // El movimiento es cambiar el pedido al que pertenece la línea.
                // El Contacto de la línea (dirección de entrega) NO se toca.
                linea.Número = destino.Número;
                linea.CabPedidoVta = destino;

                // Mantener coherentes las colecciones en memoria del objeto devuelto.
                origen?.LinPedidoVtas?.Remove(linea);
                if (destino.LinPedidoVtas == null)
                {
                    destino.LinPedidoVtas = new List<LinPedidoVta>();
                }
                if (!destino.LinPedidoVtas.Contains(linea))
                {
                    destino.LinPedidoVtas.Add(linea);
                }
            }

            // Repuntar tablas satélite que referencian la línea por su Nº_Orden (identidad
            // global), igual que el SP de FDM. Se hace una sola vez con todos los Nº_Orden.
            List<int> ordenesMovidas = lineasAMover.Select(l => l.Nº_Orden).Distinct().ToList();
            if (ordenesMovidas.Any())
            {
                foreach (PedidosEspeciale pe in db.PedidosEspeciales
                    .Where(p => p.NºOrdenVta.HasValue && ordenesMovidas.Contains(p.NºOrdenVta.Value)))
                {
                    pe.NºPedidoVta = destino.Número;
                }

                foreach (Ubicacion u in db.Ubicaciones
                    .Where(x => x.NºOrdenVta.HasValue && ordenesMovidas.Contains(x.NºOrdenVta.Value)))
                {
                    u.PedidoVta = destino.Número;
                }
            }

            // Marcamos el destino como agrupado: es el pedido contenedor que se facturará.
            destino.Agrupada = true;

            return destino;
        }
    }
}

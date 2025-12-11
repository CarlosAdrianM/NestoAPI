using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ExtractosRuta
{
    /// <summary>
    /// Servicio para gestionar inserciones en la tabla ExtractoRuta
    /// </summary>
    public class ServicioExtractoRuta : IServicioExtractoRuta
    {
        private readonly NVEntities db;

        public ServicioExtractoRuta(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Inserta un registro en ExtractoRuta copiando datos desde ExtractoCliente (para facturas).
        /// Busca el primer efecto de la factura (TipoApunte = 2, Efecto = "1") que contiene
        /// el importe que debe cobrar el repartidor y la fecha de vencimiento correcta.
        /// </summary>
        /// <param name="pedido">Pedido que se ha facturado</param>
        /// <param name="numeroFactura">Número de la factura creada</param>
        /// <param name="usuario">Usuario que realiza la operación</param>
        /// <param name="empresaFactura">Empresa donde se creó la factura (puede ser diferente a pedido.Empresa por traspaso). Si es null, usa pedido.Empresa.</param>
        /// <param name="autoSave">Si es true, guarda cambios automáticamente</param>
        public async Task InsertarDesdeFactura(CabPedidoVta pedido, string numeroFactura, string usuario, string empresaFactura = null, bool autoSave = true)
        {
            if (pedido == null)
                throw new ArgumentNullException(nameof(pedido));
            if (string.IsNullOrWhiteSpace(numeroFactura))
                throw new ArgumentException("El número de factura no puede ser null o vacío", nameof(numeroFactura));
            if (string.IsNullOrWhiteSpace(usuario))
                throw new ArgumentException("El usuario no puede ser null o vacío", nameof(usuario));

            // Usar la empresa de la factura si se proporciona, o la del pedido si no
            // IMPORTANTE: Cuando hay traspaso a empresa espejo, la factura se crea en otra empresa
            // pero el pedido sigue teniendo la empresa original
            var empresaBusqueda = !string.IsNullOrWhiteSpace(empresaFactura) ? empresaFactura : pedido.Empresa;

            // Buscar el primer efecto de la factura (TipoApunte = 2 "Cartera", Efecto = "1")
            // Este registro tiene el importe a cobrar por el repartidor y la fecha de vencimiento correcta
            var extractoEfecto = await db.ExtractosCliente
                .Where(e => e.Empresa == empresaBusqueda &&
                           e.Número == pedido.Nº_Cliente &&
                           e.Contacto == pedido.Contacto &&
                           e.Nº_Documento == numeroFactura.Trim() &&
                           e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_CARTERA &&
                           e.Efecto == "1")
                .FirstOrDefaultAsync();

            if (extractoEfecto == null)
            {
                throw new InvalidOperationException(
                    $"No se encontró el efecto en ExtractoCliente para la factura {numeroFactura}. " +
                    "La factura debe estar contabilizada antes de insertar en ExtractoRuta.");
            }

            // Crear ExtractoRuta copiando datos del efecto
            var extractoRuta = new ExtractoRuta
            {
                Empresa = extractoEfecto.Empresa,
                Nº_Orden = extractoEfecto.Nº_Orden,
                Número = extractoEfecto.Número,
                Contacto = extractoEfecto.Contacto,
                CodPostal = null, // No está en ExtractoCliente, podría obtenerse del cliente si es necesario
                Fecha = extractoEfecto.Fecha,
                Nº_Documento = numeroFactura.Trim().PadRight(10),
                Efecto = extractoEfecto.Efecto,
                Concepto = pedido.Comentarios,
                Importe = extractoEfecto.Importe,
                ImportePdte = extractoEfecto.Importe, // ImportePdte = Importe del efecto (pendiente de cobro)
                Delegación = extractoEfecto.Delegación,
                FormaVenta = extractoEfecto.FormaVenta,
                Vendedor = extractoEfecto.Vendedor,
                FechaVto = extractoEfecto.FechaVto,
                FormaPago = extractoEfecto.FormaPago,
                Ruta = pedido.Ruta,
                Estado = 0,
                TipoRuta = Constantes.ExtractoRuta.TIPO_RUTA_PEDIDO,
                Usuario = usuario,
                Fecha_Modificación = DateTime.Now
            };

            db.ExtractoRutas.Add(extractoRuta);

            if (autoSave)
            {
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Inserta un registro en ExtractoRuta usando datos del pedido (para albaranes).
        /// No existe ExtractoCliente, por lo que usa directamente datos del pedido.
        /// </summary>
        public async Task InsertarDesdeAlbaran(CabPedidoVta pedido, int numeroAlbaran, string usuario, bool autoSave = true)
        {
            if (pedido == null)
                throw new ArgumentNullException(nameof(pedido));
            if (string.IsNullOrWhiteSpace(usuario))
                throw new ArgumentException("El usuario no puede ser null o vacío", nameof(usuario));

            // Obtener cliente para datos adicionales
            var cliente = await db.Clientes
                .FindAsync(pedido.Empresa, pedido.Nº_Cliente, pedido.Contacto);

            // Obtener primera línea del pedido para datos de delegación y forma de venta
            var primeraLinea = pedido.LinPedidoVtas?.FirstOrDefault();
            if (primeraLinea == null)
            {
                throw new InvalidOperationException(
                    $"No se encontraron líneas en el pedido {pedido.Número} para obtener datos de ExtractoRuta");
            }

            // Obtener siguiente Nº_Orden negativo
            var minOrden = await db.ExtractoRutas
                .Where(e => e.Empresa == pedido.Empresa.Trim())
                .Select(e => (int?)e.Nº_Orden)
                .MinAsync() ?? 0;

            int nuevoOrdenNegativo = minOrden < 0 ? minOrden - 1 : -1;

            // Crear ExtractoRuta con datos del pedido
            var extractoRuta = new ExtractoRuta
            {
                Empresa = pedido.Empresa,
                Nº_Orden = nuevoOrdenNegativo,
                Número = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                CodPostal = cliente?.CodPostal,
                Fecha = DateTime.Now,
                Nº_Documento = numeroAlbaran.ToString().PadLeft(10),
                Efecto = null,
                Concepto = pedido.Comentarios,
                Importe = 0, // Los albaranes no tienen importe hasta que se facturen
                ImportePdte = 0,
                Delegación = primeraLinea.Delegación,
                FormaVenta = primeraLinea.Forma_Venta,
                Vendedor = pedido.Vendedor,
                FechaVto = null,
                FormaPago = pedido.Forma_Pago,
                Ruta = pedido.Ruta,
                Estado = 0,
                TipoRuta = Constantes.ExtractoRuta.TIPO_RUTA_PEDIDO,
                Usuario = usuario,
                Fecha_Modificación = DateTime.Now
            };

            db.ExtractoRutas.Add(extractoRuta);

            if (autoSave)
            {
                await db.SaveChangesAsync();
            }
        }
    }
}

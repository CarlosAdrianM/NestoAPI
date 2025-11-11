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
        /// Busca el registro de ExtractoCliente con TipoApunte = 1 (factura) y copia sus datos.
        /// </summary>
        public async Task InsertarDesdeFactura(CabPedidoVta pedido, string numeroFactura, string usuario, bool autoSave = true)
        {
            if (pedido == null)
                throw new ArgumentNullException(nameof(pedido));
            if (string.IsNullOrWhiteSpace(numeroFactura))
                throw new ArgumentException("El número de factura no puede ser null o vacío", nameof(numeroFactura));
            if (string.IsNullOrWhiteSpace(usuario))
                throw new ArgumentException("El usuario no puede ser null o vacío", nameof(usuario));

            // Buscar el ExtractoCliente de la factura (TipoApunte = 1)
            var extractoCliente = await db.ExtractosCliente
                .Where(e => e.Empresa == pedido.Empresa &&
                           e.Número == pedido.Nº_Cliente &&
                           e.Contacto == pedido.Contacto &&
                           e.Nº_Documento == numeroFactura.Trim() &&
                           e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_FACTURA)
                .FirstOrDefaultAsync();

            if (extractoCliente == null)
            {
                throw new InvalidOperationException(
                    $"No se encontró el ExtractoCliente para la factura {numeroFactura}. " +
                    "La factura debe estar contabilizada antes de insertar en ExtractoRuta.");
            }

            // Crear ExtractoRuta copiando datos del ExtractoCliente
            var extractoRuta = new ExtractoRuta
            {
                Empresa = extractoCliente.Empresa,
                Nº_Orden = extractoCliente.Nº_Orden,
                Número = extractoCliente.Número,
                Contacto = extractoCliente.Contacto,
                CodPostal = null, // No está en ExtractoCliente, podría obtenerse del cliente si es necesario
                Fecha = extractoCliente.Fecha,
                Nº_Documento = numeroFactura.Trim().PadRight(10),
                Efecto = extractoCliente.Efecto,
                Concepto = pedido.Comentarios,
                Importe = extractoCliente.Importe,
                ImportePdte = extractoCliente.ImportePdte,
                Delegación = extractoCliente.Delegación,
                FormaVenta = extractoCliente.FormaVenta,
                Vendedor = extractoCliente.Vendedor,
                FechaVto = extractoCliente.FechaVto,
                FormaPago = extractoCliente.FormaPago,
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

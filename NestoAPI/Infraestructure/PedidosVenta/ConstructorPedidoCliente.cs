using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#436: monta el <see cref="PedidoVentaDTO"/> de un pedido que crea el propio cliente
    /// desde la app, a partir de lo único que él decide (qué productos y cuántos) y de lo que ha
    /// resuelto el servidor: su ficha, los precios calculados y las condiciones de pago.
    ///
    /// <para>Es una función pura a propósito: aquí vive la decisión de qué valor lleva cada campo
    /// del pedido de la app, que es lo que hay que poder probar y leer de un vistazo. La lectura
    /// de la base de datos se queda en el controller.</para>
    /// </summary>
    public static class ConstructorPedidoCliente
    {
        /// <summary>Longitud del campo SuPedido en CabPedidoVta.</summary>
        private const int LONGITUD_SU_PEDIDO = 50;

        /// <summary>
        /// Tope del comentario del cliente. No hay límite en la base de datos (nvarchar(max)),
        /// pero el comentario acaba en el correo del pedido y en el picking: un cliente no tiene
        /// por qué poder meter ahí un texto sin fin.
        /// </summary>
        internal const int LONGITUD_COMENTARIOS = 500;

        /// <summary>Tope de líneas por pedido, para que una sola petición no dé trabajo sin fin.</summary>
        internal const int MAXIMO_LINEAS = 100;

        /// <summary>
        /// El pedido de la app se guarda a nombre de un usuario que dice canal y cliente, para que
        /// en la auditoría se distinga de un pedido metido por un empleado. Sale del cliente que
        /// viene en el JWT, nunca del cuerpo de la petición: es lo que impide pedir en nombre de
        /// otro. Cabe de sobra en el campo Usuario, que es varchar(30).
        /// </summary>
        public static string UsuarioDelPedido(string cliente)
        {
            return Constantes.FormasVenta.APP + "\\" + (cliente ?? string.Empty).Trim();
        }

        /// <param name="peticion">Lo que ha pedido el cliente.</param>
        /// <param name="cliente">Su ficha, ya resuelta a partir del JWT.</param>
        /// <param name="precios">Precio y descuento calculados por el servidor, por producto.</param>
        /// <param name="formaPago">La que ha autorizado la política del canal.</param>
        /// <param name="plazosPago">Los que ha autorizado la política del canal.</param>
        /// <param name="fecha">Fecha del pedido (se inyecta para poder probarlo).</param>
        public static PedidoVentaDTO Construir(
            PedidoClienteRequest peticion,
            ClienteDTO cliente,
            IDictionary<string, ProductoPlantillaDTO> precios,
            string formaPago,
            string plazosPago,
            DateTime fecha)
        {
            if (peticion == null)
            {
                throw new ArgumentNullException(nameof(peticion));
            }
            if (cliente == null)
            {
                throw new ArgumentNullException(nameof(cliente));
            }

            string empresa = string.IsNullOrWhiteSpace(cliente.empresa)
                ? Constantes.Empresas.EMPRESA_POR_DEFECTO
                : cliente.empresa.Trim();
            string usuario = UsuarioDelPedido(cliente.cliente);

            PedidoVentaDTO pedido = new PedidoVentaDTO
            {
                empresa = empresa,
                // Del JWT, nunca del cuerpo: es lo que impide pedir en nombre de otro
                cliente = cliente.cliente?.Trim(),
                contacto = cliente.contacto?.Trim(),
                fecha = fecha.Date,
                formaPago = formaPago,
                plazosPago = plazosPago,
                // De la ficha del cliente (ya venían en ClienteDTO por CanalesExternos)
                iva = cliente.iva,
                ccc = cliente.ccc,
                periodoFacturacion = string.IsNullOrWhiteSpace(cliente.periodoFacturacion)
                    ? Constantes.Pedidos.PERIODO_FACTURACION_NORMAL
                    : cliente.periodoFacturacion.Trim(),
                ruta = string.IsNullOrWhiteSpace(cliente.ruta)
                    ? Constantes.Pedidos.RUTA_AGENCIA_00
                    : cliente.ruta.Trim(),
                servirJunto = cliente.servirJunto,
                mantenerJunto = cliente.mantenerJunto,
                noComisiona = cliente.noComisiona,
                vendedor = cliente.vendedor?.Trim(),
                comentarioPicking = cliente.comentarioPicking,
                // Fijos del canal (NestoAPI#435)
                serie = Constantes.Series.SERIE_POR_DEFECTO,
                origen = empresa,
                // Lo que dice el cliente, recortado
                comentarios = Truncar(peticion.Comentarios, LONGITUD_COMENTARIOS),
                suPedido = Truncar(peticion.SuPedido, LONGITUD_SU_PEDIDO),
                Usuario = usuario,
                // Los portes los calcula el servidor: la app no puede suprimirlos (y aunque mandara
                // false, DebeAnadirPortes solo hace caso a Almacén y Compras).
                AnadirPortes = true,
                notaEntrega = false,
                EsPresupuesto = false,
                CreadoSinPasarValidacion = false
            };

            foreach (LineaPedidoClienteRequest lineaPedida in peticion.Lineas ?? new List<LineaPedidoClienteRequest>())
            {
                pedido.Lineas.Add(ConstruirLinea(lineaPedida, precios, fecha, usuario));
            }

            return pedido;
        }

        private static LineaPedidoVentaDTO ConstruirLinea(
            LineaPedidoClienteRequest lineaPedida,
            IDictionary<string, ProductoPlantillaDTO> precios,
            DateTime fecha,
            string usuario)
        {
            string producto = lineaPedida.Producto?.Trim();
            ProductoPlantillaDTO precio = null;
            _ = precios != null && producto != null && precios.TryGetValue(producto, out precio);

            return new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                Producto = producto,
                texto = precio?.nombre,
                Cantidad = lineaPedida.Cantidad,
                fechaEntrega = fecha.Date,
                // El precio y el descuento los pone el servidor (GestorPrecios), igual que en
                // GET api/Productos?cliente=&contacto=&cantidad=. Lo que llegue en la petición no
                // se mira: un cliente no puede ponerse su propio precio.
                PrecioUnitario = precio?.precio ?? 0,
                DescuentoProducto = precio?.descuento ?? 0,
                AplicarDescuento = precio?.aplicarDescuento ?? true,
                DescuentoLinea = 0,
                iva = precio?.iva,
                almacen = Constantes.Almacenes.ALGETE,
                delegacion = Constantes.Empresas.DELEGACION_POR_DEFECTO,
                formaVenta = Constantes.FormasVenta.APP,
                usuario = usuario,
                vistoBueno = true
            };
        }

        /// <summary>
        /// NestoAPI#436: lo que el cliente puede pedir mal. Devuelve null si la petición es válida,
        /// o el mensaje de qué está mal para responder un BadRequest.
        /// </summary>
        public static string ValidarPeticion(PedidoClienteRequest peticion)
        {
            if (peticion == null)
            {
                return "No se ha recibido ningún pedido";
            }
            if (peticion.Lineas == null || !peticion.Lineas.Any())
            {
                return "El pedido tiene que llevar alguna línea";
            }
            if (peticion.Lineas.Count > MAXIMO_LINEAS)
            {
                // Un carrito de verdad no tiene cien líneas. El tope evita que una petición sola
                // ponga a calcular precios y stock hasta reventar (relacionado con NestoAPI#428).
                return $"El pedido no puede tener más de {MAXIMO_LINEAS} líneas";
            }
            if (peticion.Lineas.Any(l => string.IsNullOrWhiteSpace(l.Producto)))
            {
                return "Todas las líneas tienen que llevar producto";
            }
            LineaPedidoClienteRequest cantidadNoValida = peticion.Lineas.FirstOrDefault(l => l.Cantidad <= 0);
            if (cantidadNoValida != null)
            {
                return $"La cantidad del producto {cantidadNoValida.Producto?.Trim()} tiene que ser mayor que cero";
            }
            IEnumerable<string> repetidos = peticion.Lineas
                .GroupBy(l => l.Producto.Trim().ToUpperInvariant())
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            if (repetidos.Any())
            {
                // Un carrito manda una línea por producto. Si llegan dos del mismo, o es un error
                // de la app o alguien está probando a colar dos precios distintos.
                return $"El producto {repetidos.First()} viene repetido en el pedido";
            }
            return null;
        }

        private static string Truncar(string texto, int longitud)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            texto = texto.Trim();
            return texto.Length <= longitud ? texto : texto.Substring(0, longitud);
        }
    }
}

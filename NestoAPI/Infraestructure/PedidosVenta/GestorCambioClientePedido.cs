using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using System.Web.Http;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#519: cómo ha ido el cambio de cliente (el controller lo traduce a HTTP).</summary>
    public class ResultadoCambioClientePedido
    {
        public bool NoEncontrado { get; set; }
        /// <summary>Por qué no se ha hecho, en lenguaje de usuario. Null si se ha hecho.</summary>
        public string Error { get; set; }
        public CambiarClientePedidoRespuesta Respuesta { get; set; }
    }

    /// <summary>
    /// NestoAPI#519: pasa un pedido que todavía no ha salido a otro cliente. Las decisiones están en
    /// <see cref="CambioClientePedido"/>; aquí se lee, se guarda y se deja rastro.
    ///
    /// <para>Por qué no basta con el PUT de siempre: el PUT acepta un <c>Nº_Cliente</c> distinto pero solo lo
    /// copia a la cabecera y a las líneas; no recalcula nada más (contacto de cobro, condiciones de pago, CCC,
    /// IVA de las líneas, descuento de cliente, pronto pago, vendedor por grupo, precios) ni comprueba nada.
    /// Y además lo guarda todo en un solo SaveChanges, y el trigger <c>trgCabPedidoVtaUpd</c> rechaza cambiar el
    /// cliente de la cabecera mientras haya líneas de otro cliente («No se puede modificar el cliente porque ya
    /// tiene lineas»), así que depende del orden en que EF mande los UPDATE.</para>
    ///
    /// <para>Aquí: primero las líneas, luego la cabecera y, al final, un PUT del pedido releído, que es el que
    /// recalcula los portes (código postal y ruta del cliente nuevo) y manda el correo de modificación. Todo
    /// dentro de una transacción: si algo falla (la validación con el cliente nuevo, un trigger, el PUT), el
    /// pedido se queda como estaba.</para>
    /// </summary>
    public class GestorCambioClientePedido
    {
        private readonly NVEntities db;

        public GestorCambioClientePedido(NVEntities db, Func<PedidoVentaDTO, Task<IHttpActionResult>> guardarPedido)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            GuardarPedido = guardarPedido ?? throw new ArgumentNullException(nameof(guardarPedido));
        }

        /// <summary>El PUT del controller (portes, correo de modificación y el resto de reglas del guardado).</summary>
        internal Func<PedidoVentaDTO, Task<IHttpActionResult>> GuardarPedido { get; set; }

        /// <summary>Lee el pedido como lo ve un cliente (Nesto). Con su propio contexto: dentro de la transacción.</summary>
        internal Func<string, int, Task<PedidoVentaDTO>> LeerPedido { get; set; } = GestorPedidosVenta.LeerPedido;

        /// <summary>Precio de cliente de un producto (empresa, producto, cliente, contacto, cantidad).</summary>
        internal Func<string, string, string, string, short, Task<ProductoPlantillaDTO>> CalcularPrecio { get; set; }

        /// <summary>Recalcula los importes de una línea con el IVA de cabecera que se le pasa.</summary>
        internal Action<LinPedidoVta, string> CalcularImportes { get; set; }

        internal Func<PedidoVentaDTO, RespuestaValidacion> Validar { get; set; } = GestorPrecios.EsPedidoValido;

        /// <summary>Si quien hace el cambio puede seguir aunque el pedido no pase la validación (como en el PUT).</summary>
        internal Func<RespuestaValidacion, bool> PuedeOmitirValidacion { get; set; } = _ => false;

        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        /// <summary>En los tests (BD falsa) no hay transacción que abrir.</summary>
        internal bool UsarTransaccion { get; set; } = true;

        /// <summary>Lo que se deja en ELMAH como rastro informativo (sustituible en los tests).</summary>
        internal Action<Exception> RegistrarEnElmah { get; set; } = ElmahHelper.Log;

        public async Task<ResultadoCambioClientePedido> CambiarCliente(string empresa, int numero, CambiarClientePedidoRequest peticion, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                return Error("Falta el cliente al que pasa el pedido.");
            }
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string clienteNuevo = peticion.Cliente.Trim();
            string contactoNuevo = peticion.Contacto?.Trim();

            CabPedidoVta cab = await db.CabPedidoVtas.Include(c => c.LinPedidoVtas)
                .SingleOrDefaultAsync(c => c.Empresa == empresa && c.Número == numero).ConfigureAwait(false);
            if (cab == null)
            {
                return new ResultadoCambioClientePedido { NoEncontrado = true };
            }

            Cliente ficha = string.IsNullOrWhiteSpace(contactoNuevo)
                ? await db.Clientes.BuscarPrincipalAsync(empresa, clienteNuevo).ConfigureAwait(false)
                : await db.Clientes.SingleOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == clienteNuevo && c.Contacto == contactoNuevo).ConfigureAwait(false);
            string motivoFicha = CambioClientePedido.MotivoFichaNoValida(ficha, clienteNuevo, contactoNuevo);
            if (motivoFicha != null)
            {
                return Error(motivoFicha);
            }
            string clienteAnterior = cab.Nº_Cliente?.Trim();
            string contactoAnterior = cab.Contacto?.Trim();
            if (clienteAnterior == ficha.Nº_Cliente.Trim() && contactoAnterior == ficha.Contacto.Trim())
            {
                return Error($"El pedido {numero} ya es del cliente {clienteAnterior}/{contactoAnterior}.");
            }

            string motivo = await MotivoNoSePuede(cab).ConfigureAwait(false);
            if (motivo != null)
            {
                return Error(motivo);
            }

            PedidoVentaDTO antes = await LeerPedido(empresa, numero).ConfigureAwait(false);

            List<CondPagoCliente> condicionesFicha = await db.CondPagoClientes
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == ficha.Nº_Cliente && c.Contacto == ficha.Contacto)
                .ToListAsync().ConfigureAwait(false);
            CondPagoCliente condiciones = CambioClientePedido.ResolverCondicionesPago(condicionesFicha, antes?.Total ?? 0);
            if (condiciones == null)
            {
                return Error($"La ficha del cliente {ficha.Nº_Cliente.Trim()}/{ficha.Contacto.Trim()} no tiene condiciones de pago (forma y plazos). Póngaselas antes de pasarle el pedido.");
            }
            FormaPago formaPago = await db.FormasPago
                .SingleOrDefaultAsync(f => f.Empresa == empresa && f.Número == condiciones.FormaPago).ConfigureAwait(false);
            bool cccObligatorio = formaPago?.CCCObligatorio == true;
            if (cccObligatorio && string.IsNullOrWhiteSpace(ficha.CCC))
            {
                return Error($"La forma de pago {condiciones.FormaPago?.Trim()} de la ficha del cliente {ficha.Nº_Cliente.Trim()}/{ficha.Contacto.Trim()} necesita una cuenta bancaria y la ficha no tiene CCC.");
            }
            PlazoPago plazos = await db.PlazosPago
                .SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == condiciones.PlazosPago).ConfigureAwait(false);
            DescuentosCliente descuentoCliente = await db.DescuentosClientes
                .Where(d => d.Empresa == empresa && d.Nº_Cliente == ficha.Nº_Cliente && d.Contacto == ficha.Contacto)
                .OrderBy(d => d.ImporteMínimo)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            string ivaNuevo = string.IsNullOrWhiteSpace(ficha.IVA) ? null : ficha.IVA;

            // Precios del cliente nuevo, solo para las líneas «normales» (ver SeRecalculaElPrecio)
            Dictionary<int, ProductoPlantillaDTO> precios = new Dictionary<int, ProductoPlantillaDTO>();
            foreach (LinPedidoVta linea in cab.LinPedidoVtas.Where(CambioClientePedido.SeRecalculaElPrecio))
            {
                ProductoPlantillaDTO precio = await CalcularPrecio(empresa, linea.Producto.Trim(), ficha.Nº_Cliente.Trim(),
                    ficha.Contacto.Trim(), linea.Cantidad ?? 0).ConfigureAwait(false);
                if (precio != null)
                {
                    precios[linea.Nº_Orden] = precio;
                }
            }

            // Un pedido sin IVA que pasa a un cliente con IVA: las líneas de producto necesitan su código de IVA
            Dictionary<string, string> ivaProductos = new Dictionary<string, string>();
            if (ivaNuevo != null)
            {
                List<string> sinIva = cab.LinPedidoVtas
                    .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && string.IsNullOrWhiteSpace(l.IVA) && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto)
                    .Distinct()
                    .ToList();
                if (sinIva.Any())
                {
                    ivaProductos = (await db.Productos
                        .Where(p => p.Empresa == empresa && sinIva.Contains(p.Número))
                        .Select(p => new { p.Número, p.IVA_Repercutido })
                        .ToListAsync().ConfigureAwait(false))
                        .GroupBy(p => p.Número.Trim())
                        .ToDictionary(g => g.Key, g => g.First().IVA_Repercutido);
                }
            }

            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            string anteriorParaAuditoria = JsonConvert.SerializeObject(Resumen(cab));

            foreach (LinPedidoVta linea in cab.LinPedidoVtas)
            {
                _ = precios.TryGetValue(linea.Nº_Orden, out ProductoPlantillaDTO precioNuevo);
                _ = CambioClientePedido.AplicarALinea(linea, ficha.Nº_Cliente, ficha.Contacto,
                    descuentoCliente?.Descuento ?? 0, plazos?.DtoProntoPago ?? 0, precioNuevo);
                if (ivaNuevo != null && string.IsNullOrWhiteSpace(linea.IVA) && linea.Producto != null
                    && ivaProductos.TryGetValue(linea.Producto.Trim(), out string ivaProducto))
                {
                    linea.IVA = ivaProducto;
                }
                if (linea.TipoLinea != null && linea.TipoLinea != Constantes.TiposLineaVenta.TEXTO)
                {
                    CalcularImportes(linea, ivaNuevo);
                }
            }

            // Vendedores por grupo de producto: los del cliente nuevo, como al crear el pedido
            List<VendedorPedidoGrupoProducto> vendedoresPedido = await db.VendedoresPedidosGruposProductos
                .Where(v => v.Empresa == empresa && v.Pedido == numero).ToListAsync().ConfigureAwait(false);
            _ = db.VendedoresPedidosGruposProductos.RemoveRange(vendedoresPedido);
            GestorComisiones.CrearVendedorPedidoGrupoProducto(db,
                new CabPedidoVta { Empresa = cab.Empresa, Número = numero, Nº_Cliente = ficha.Nº_Cliente, Contacto = ficha.Contacto },
                new PedidoVentaDTO { Usuario = usuarioAuditoria });

            PedidoVentaDTO despues;
            using (TransactionScope transaccion = UsarTransaccion
                ? new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { Timeout = TimeSpan.FromMinutes(5) }, TransactionScopeAsyncFlowOption.Enabled)
                : null)
            {
                // 1. Las líneas primero: trgCabPedidoVtaUpd no deja cambiar el cliente de la cabecera con líneas de otro
                _ = await db.SaveChangesAsync().ConfigureAwait(true);

                // 2. La cabecera
                CambioClientePedido.AplicarACabecera(cab, ficha, condiciones, cccObligatorio, usuarioAuditoria, Ahora());
                _ = db.Modificaciones.Add(new Modificacion
                {
                    Tabla = "Pedidos",
                    Anterior = anteriorParaAuditoria,
                    Nuevo = JsonConvert.SerializeObject(new { Operacion = "CambiarCliente (#519)", Datos = Resumen(cab) }),
                    Usuario = usuarioAuditoria,
                    Fecha = Ahora()
                });
                _ = await db.SaveChangesAsync().ConfigureAwait(true);

                // 3. Se valida como un pedido del cliente nuevo (ofertas y descuentos permitidos, precios...)
                PedidoVentaDTO releido = await LeerPedido(empresa, numero).ConfigureAwait(true);
                releido.Usuario = usuario;
                RespuestaValidacion validacion = Validar(releido);
                if (validacion != null && !validacion.ValidacionSuperada
                    && !(peticion.CreadoSinPasarValidacion && PuedeOmitirValidacion(validacion)))
                {
                    throw new PedidoValidacionException(validacion.Motivo, validacion, empresa, numero, ficha.Nº_Cliente?.Trim(), usuario);
                }
                releido.CreadoSinPasarValidacion = peticion.CreadoSinPasarValidacion;

                // 4. El PUT de siempre: portes del cliente nuevo y correo de modificación
                string errorGuardado = GestorPedidosVenta.MensajeDeErrorDelGuardado(await GuardarPedido(releido).ConfigureAwait(true));
                if (errorGuardado != null)
                {
                    throw new NestoBusinessException($"No se ha podido cambiar el cliente del pedido {numero}: {errorGuardado}");
                }

                despues = await LeerPedido(empresa, numero).ConfigureAwait(true);
                transaccion?.Complete();
            }

            List<string> cambios = CambioClientePedido.DescribirCambios(antes, despues);
            RegistrarEnElmah(new Exception($"[Cambio de cliente #519] Pedido {empresa}/{numero}: {clienteAnterior}/{contactoAnterior} → " +
                $"{ficha.Nº_Cliente.Trim()}/{ficha.Contacto.Trim()} por {usuarioAuditoria}. {string.Join(". ", cambios)}"));

            return new ResultadoCambioClientePedido
            {
                Respuesta = new CambiarClientePedidoRespuesta
                {
                    Empresa = empresa,
                    Numero = numero,
                    ClienteAnterior = clienteAnterior,
                    ContactoAnterior = contactoAnterior,
                    Cliente = ficha.Nº_Cliente.Trim(),
                    Contacto = ficha.Contacto.Trim(),
                    Cambios = cambios
                }
            };
        }

        private async Task<string> MotivoNoSePuede(CabPedidoVta cab)
        {
            string empresa = cab.Empresa;
            int numero = cab.Número;
            int prepagosVivos = await db.Prepagos
                .CountAsync(p => p.Empresa == empresa && p.Pedido == numero && p.Factura == null).ConfigureAwait(false);
            int efectos = await db.EfectosPedidosVentas
                .CountAsync(e => e.Empresa == empresa && e.Pedido == numero).ConfigureAwait(false);
            List<int> envios = await db.EnviosAgencias
                .Where(e => e.Empresa == empresa && e.Pedido == numero)
                .Select(e => e.Numero)
                .ToListAsync().ConfigureAwait(false);
            string documento = numero.ToString();
            int pagosTarjeta = await db.PagosTPV
                .CountAsync(p => p.Empresa == empresa && p.Documento == documento
                    && (p.Estado == Constantes.EstadosPagoTPV.PENDIENTE || p.Estado == Constantes.EstadosPagoTPV.AUTORIZADO)).ConfigureAwait(false);
            return CambioClientePedido.MotivoNoSePuede(numero, cab.NotaEntrega, cab.LinPedidoVtas,
                prepagosVivos, efectos, envios, pagosTarjeta);
        }

        /// <summary>Lo que se guarda en Modificaciones: lo que depende del cliente, antes y después.</summary>
        private static object Resumen(CabPedidoVta cab)
        {
            return new
            {
                Empresa = cab.Empresa?.Trim(),
                Pedido = cab.Número,
                Cliente = cab.Nº_Cliente?.Trim(),
                Contacto = cab.Contacto?.Trim(),
                cab.ContactoCobro,
                cab.IVA,
                FormaPago = cab.Forma_Pago,
                cab.PlazosPago,
                cab.CCC,
                cab.Vendedor,
                cab.Ruta,
                PeriodoFacturacion = cab.Periodo_Facturacion,
                cab.MantenerJunto,
                cab.ModoFacturacion,
                cab.NoComisiona,
                Lineas = cab.LinPedidoVtas?.Select(l => new
                {
                    l.Nº_Orden,
                    Producto = l.Producto?.Trim(),
                    l.Cantidad,
                    l.Precio,
                    l.DescuentoCliente,
                    l.DescuentoProducto,
                    l.Descuento,
                    l.DescuentoPP,
                    l.IVA,
                    l.Base_Imponible,
                    l.Total
                })
            };
        }

        private static ResultadoCambioClientePedido Error(string mensaje)
        {
            return new ResultadoCambioClientePedido { Error = mensaje };
        }
    }
}

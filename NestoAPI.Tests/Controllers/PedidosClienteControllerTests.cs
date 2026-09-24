using System.Collections.Generic;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Tests.Helpers;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#436: quién puede crear un pedido de cliente y con qué. El cliente sale SIEMPRE del
    /// JWT: si viniera en el cuerpo se ignoraría, y quien no es un cliente no entra aquí.
    /// </summary>
    [TestClass]
    public class PedidosClienteControllerTests
    {
        #region TNV#66: la lista de pedidos del cliente

        [TestMethod]
        public async Task GetPedidosCliente_SinClaimDeCliente_NoEntra()
        {
            // Un empleado o un vendedor no tienen claim "cliente": esta lista es la de un cliente
            // final viendo SUS pedidos, y quien no lo es no puede pedirla.
            PedidosClienteController controller = ControllerConIdentity(new ClaimsIdentity(new List<Claim>(), "JWT"));

            IHttpActionResult resultado = await controller.GetPedidosCliente();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task GetPedidosCliente_SinAutenticar_NoEntra()
        {
            PedidosClienteController controller = ControllerConIdentity(null);

            IHttpActionResult resultado = await controller.GetPedidosCliente();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task GetPedidosCliente_DiasFueraDeRango_NoSeTraeElHistoricoEntero()
        {
            PedidosClienteController controller = ControllerConIdentity(IdentityDeCliente("15816"));

            IHttpActionResult demasiados = await controller.GetPedidosCliente(
                PedidosClienteController.MAXIMO_DIAS_DE_PEDIDOS + 1);
            IHttpActionResult ninguno = await controller.GetPedidosCliente(0);

            Assert.IsInstanceOfType(demasiados, typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(ninguno, typeof(BadRequestErrorMessageResult));
        }

        private static ClaimsIdentity IdentityDeCliente(string cliente)
        {
            return new ClaimsIdentity(new List<Claim> { new Claim("cliente", cliente) }, "JWT");
        }

        private static PedidosClienteController ControllerConIdentity(ClaimsIdentity identity)
        {
            PedidosClienteController controller = new PedidosClienteController(
                A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                RequestContext = new HttpRequestContext
                {
                    Principal = identity == null ? null : new ClaimsPrincipal(identity)
                }
            };
            return controller;
        }

        #endregion

        #region NestoAPI#452: el cobro directo (MIT) cobraba la base imponible, sin IVA ni portes

        [TestMethod]
        public void RellenarPorcentajesIva_AntesDeCobrar_ElTotalDejaDeSerLaBasePelada()
        {
            // 03/09/26 en producción: el cobro directo pidió 0,75 EUR (la base) y el pedido valía
            // 0,91 EUR. El DTO recién construido solo trae el CÓDIGO de IVA ("G21"); el porcentaje
            // se rellenaba dentro de PostPedidoVenta, que va DESPUÉS del cobro.
            PedidoVentaDTO pedido = PedidoDeLaApp(precio: 0.75M, codigoIva: "G21");

            Assert.AreEqual(0.75M, pedido.Total, "sin porcentaje de IVA, el total es la base: el fallo");

            PedidosClienteController.RellenarPorcentajesIva(pedido);

            Assert.AreEqual(0.91M, pedido.Total, "con el 21 % ya es el importe que hay que cobrar");
            Assert.AreEqual(0.75M, pedido.BaseImponible, "la base no cambia");
        }

        [TestMethod]
        public void RellenarPorcentajesIva_SinParametros_NoRevienta()
        {
            PedidoVentaDTO pedido = PedidoDeLaApp(0.75M, "G21");
            pedido.ParametrosIva = new List<ParametrosIvaBase>();

            PedidosClienteController.RellenarPorcentajesIva(pedido);

            Assert.AreEqual(0.75M, pedido.Total);
        }

        [TestMethod]
        public void DiferenciaCobroPedido_LoCobradoYElPedidoCoinciden_NoAvisa()
        {
            Assert.IsNull(PedidosClienteController.DiferenciaCobroPedido(0.91M, 0.91M, 925347, "F1CC0DC15191"));
        }

        [TestMethod]
        public void DiferenciaCobroPedido_SeCobraDeMenos_AvisaConLosDosImportes()
        {
            string aviso = PedidosClienteController.DiferenciaCobroPedido(0.75M, 0.91M, 925347, "418F0BC15191");

            Assert.IsNotNull(aviso);
            StringAssert.Contains(aviso, "de MENOS");
            StringAssert.Contains(aviso, "0,75");
            StringAssert.Contains(aviso, "0,91");
            StringAssert.Contains(aviso, "925347");
            StringAssert.Contains(aviso, "418F0BC15191");
        }

        [TestMethod]
        public void DiferenciaCobroPedido_SeCobraDeMas_TambienAvisa()
        {
            string aviso = PedidosClienteController.DiferenciaCobroPedido(10M, 9M, 925347, "X");

            Assert.IsNotNull(aviso);
            StringAssert.Contains(aviso, "de MÁS");
        }

        private static PedidoVentaDTO PedidoDeLaApp(decimal precio, string codigoIva)
        {
            return new PedidoVentaDTO
            {
                empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                cliente = "15191",
                iva = "G",
                ParametrosIva = new List<ParametrosIvaBase>
                {
                    new ParametrosIvaBase
                    {
                        CodigoIvaProducto = codigoIva,
                        PorcentajeIvaProducto = 0.21M,
                        PorcentajeRecargoEquivalencia = 0M
                    }
                },
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO
                    {
                        tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                        Producto = "12345",
                        Cantidad = 1,
                        PrecioUnitario = precio,
                        iva = codigoIva
                    }
                }
            };
        }

        #endregion

        #region NestoAPI#524: la política de precios ocultos llegaba al pedido real

        [TestMethod]
        public async Task CalcularPrecios_PersonaQueNoVeLosPrecios_ElPedidoLlevaElPrecioRealDelCliente()
        {
            // Pedido 926936 (23/09/26): una persona con cargo 31 hizo el pedido y se guardó a
            // tarifa y sin descuentos. El controller de productos creado con new NO va sin usuario:
            // su User cae a Thread.CurrentPrincipal, que en producción es el del JWT. Con el cargo
            // 30 el precio habría sido 0.
            foreach (string nivel in new[] { "SinDescuentos", "SinPrecios" })
            {
                System.Security.Principal.IPrincipal anterior = Thread.CurrentPrincipal;
                try
                {
                    Claim[] claims = { new Claim("cliente", "15191"), new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, nivel) };
                    Thread.CurrentPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"));
                    PedidosClienteController controller = ControllerConIdentidad(claims);
                    controller.CrearControllerProductos = () => ControllerProductosConPrecioDeCliente(
                        new Producto { Empresa = "1", Número = "39667", Nombre = "MASCARILLA", PVP = 17.95M, Aplicar_Dto = true, IVA_Repercutido = "G21" },
                        precioCliente: 17.95M, descuentoCliente: 0.65M);

                    Dictionary<string, ProductoPlantillaDTO> precios = await controller.CalcularPrecios("1",
                        new ClienteDTO { cliente = "15191", contacto = "2" },
                        new List<LineaPedidoClienteRequest> { new LineaPedidoClienteRequest { Producto = "39667", Cantidad = 1 } });

                    ProductoPlantillaDTO precio = precios["39667"];
                    Assert.AreEqual(17.95M, precio.precio, nivel + ": el precio de cliente, nunca 0");
                    Assert.AreEqual(0.65M, precio.descuento, nivel + ": su descuento");
                    Assert.IsTrue(precio.aplicarDescuento, nivel + ": Aplicar Dto de la ficha");
                }
                finally
                {
                    Thread.CurrentPrincipal = anterior;
                }
            }
        }

        [TestMethod]
        public async Task GetProducto_PersonaQueNoVeLosDescuentos_SigueSinVerlos()
        {
            // El endpoint público sigue ocultando: lo que cambia es solo lo que va al pedido.
            ProductosController controller = ControllerProductosConPrecioDeCliente(
                new Producto { Empresa = "1", Número = "39667", Nombre = "MASCARILLA", PVP = 17.95M, Aplicar_Dto = true, IVA_Repercutido = "G21" },
                precioCliente: 17.95M, descuentoCliente: 0.65M);
            controller.RequestContext = new HttpRequestContext
            {
                Principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("cliente", "15191"),
                    new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, "SinDescuentos")
                }, "JWT"))
            };

            var resultado = await controller.GetProducto("1", "39667", "15191", "2", 1) as OkNegotiatedContentResult<ProductoPlantillaDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(0M, resultado.Content.descuento);
            Assert.IsFalse(resultado.Content.aplicarDescuento);
            Assert.IsTrue(resultado.Content.descuentoOculto);
        }

        private static ProductosController ControllerProductosConPrecioDeCliente(Producto producto, decimal precioCliente, decimal descuentoCliente)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<Producto> productos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            IQueryable<Producto> datos = new List<Producto> { producto }.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<Producto>)productos).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<Producto>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<Producto>)productos).Provider).Returns(new TestDbAsyncQueryProvider<Producto>(datos.Provider));
            A.CallTo(() => ((IQueryable<Producto>)productos).Expression).Returns(datos.Expression);
            A.CallTo(() => ((IQueryable<Producto>)productos).ElementType).Returns(datos.ElementType);
            A.CallTo(() => ((IQueryable<Producto>)productos).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => db.Productos).Returns(productos);

            return new ProductosController(db, A.Fake<IGestorSincronizacion>())
            {
                // GestorPrecios va a la base de datos: aquí, el precio de cliente ya calculado
                CalcularDescuentoProducto = p =>
                {
                    p.precioCalculado = precioCliente;
                    p.descuentoCalculado = descuentoCliente;
                }
            };
        }

        #endregion

        private static PedidosClienteController ControllerConIdentidad(params Claim[] claims)
        {
            PedidosClienteController controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                RequestContext = new HttpRequestContext
                {
                    Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"))
                }
            };
            return controller;
        }

        private static PedidoClienteRequest PeticionValida()
        {
            return new PedidoClienteRequest
            {
                Lineas = new List<LineaPedidoClienteRequest>
                {
                    new LineaPedidoClienteRequest { Producto = "12345", Cantidad = 1 }
                }
            };
        }

        [TestMethod]
        public async Task PostPortesCliente_UsuarioSinPrecios_NoCalculaPortes()
        {
            // NestoAPI#446: "te faltan X € para el envío gratis" es un importe
            foreach (string nivel in new[] { "SinPrecios", "SinDescuentos" })
            {
                PedidosClienteController controller = ControllerConIdentidad(
                    new Claim("cliente", "15191"), new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, nivel));

                var resultado = await controller.PostPortesCliente(PeticionValida());

                var badRequest = resultado as BadRequestErrorMessageResult;
                Assert.IsNotNull(badRequest, nivel);
                Assert.AreEqual(PoliticaPreciosOcultos.MOTIVO_PORTES, badRequest.Message);
            }
        }

        [TestMethod]
        public async Task PostPedidoCliente_SinTokenDeCliente_NoAutorizado()
        {
            PedidosClienteController controller = ControllerConIdentidad();

            var resultado = await controller.PostPedidoCliente(PeticionValida());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_TokenDeEmpleado_NoAutorizado()
        {
            // Un empleado no tiene claim "cliente": lo suyo es POST api/PedidosVenta, el de siempre.
            PedidosClienteController controller = ControllerConIdentidad(new Claim("IsEmployee", "true"));

            var resultado = await controller.PostPedidoCliente(PeticionValida());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_TokenDeVendedor_NoAutorizado()
        {
            // ValidadorAccesoCliente deja hoy fuera a los vendedores; aquí además no traen cliente.
            PedidosClienteController controller = ControllerConIdentidad(
                new Claim("IsVendedor", "true"), new Claim("Vendedor", "CM"));

            var resultado = await controller.PostPedidoCliente(PeticionValida());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_ClienteSinLineas_DevuelveBadRequestSinTocarLaBaseDeDatos()
        {
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));

            var resultado = await controller.PostPedidoCliente(new PedidoClienteRequest());

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_CantidadCero_DevuelveBadRequest()
        {
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));
            PedidoClienteRequest peticion = PeticionValida();
            foreach (LineaPedidoClienteRequest linea in peticion.Lineas)
            {
                linea.Cantidad = 0;
            }

            var resultado = await controller.PostPedidoCliente(peticion);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
        [TestMethod]
        public void CrearControllerPedidos_LaPeticionLlevaSuContextoDentro_SeCableaSinReventar()
        {
            // Regresión 01/09/26: en producción el HttpRequestMessage lleva dentro su
            // HttpRequestContext (cosa que el resto de tests no tiene, porque su Request es null).
            // Asignar Request antes que RequestContext lanzaba ArgumentException ("la propiedad de
            // contexto de solicitud debe tener un valor nulo o coincidir con
            // ApiController.RequestContext") y ningún cliente podía crear un pedido.
            ClaimsPrincipal principal = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim("cliente", "15191") }, "JWT"));
            HttpConfiguration configuration = new HttpConfiguration();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/Pedidos/Cliente");
            HttpRequestContext contexto = new HttpRequestContext { Principal = principal, Configuration = configuration };
            request.SetRequestContext(contexto);
            PedidosClienteController controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                Configuration = configuration,
                RequestContext = contexto,
                Request = request
            };

            PedidosVentaController delegado = controller.CrearControllerPedidos();

            Assert.AreSame(request, delegado.Request);
            Assert.AreSame(principal, delegado.RequestContext.Principal);
        }

        // NestoAPI#436 (aviso del equipo de la app): el calculo de portes del carrito va por el
        // mismo camino, asi que tiene las mismas reglas de acceso.

        [TestMethod]
        public async Task PostPortesCliente_SinTokenDeCliente_NoAutorizado()
        {
            PedidosClienteController controller = ControllerConIdentidad();

            var resultado = await controller.PostPortesCliente(PeticionValida());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPortesCliente_CarritoVacio_DevuelveBadRequest()
        {
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));

            var resultado = await controller.PostPortesCliente(new PedidoClienteRequest());

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        // TNV#68: el cobro del carrito, que va ANTES de crear el pedido. Tiene las mismas reglas
        // de acceso que el resto del canal, y ademas exige la tarjeta con la que se va a cobrar.

        [TestMethod]
        public async Task PostPagoCarrito_SinTokenDeCliente_NoAutorizado()
        {
            PedidosClienteController controller = ControllerConIdentidad();
            PedidoClienteRequest peticion = PeticionValida();
            peticion.TarjetaId = 7;

            var resultado = await controller.PostPagoCarrito(peticion);

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task PostPagoCarrito_SinTarjeta_NoSeCobraNada()
        {
            // Sin tarjeta no hay nada que cobrar: es lo unico que la app tiene que decir.
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));

            var resultado = await controller.PostPagoCarrito(PeticionValida());

            var badRequest = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequest);
            StringAssert.Contains(badRequest.Message, "TarjetaId");
        }

        [TestMethod]
        public async Task PostPagoCarrito_UsuarioSinPrecios_NoPagaConTarjeta()
        {
            // NestoAPI#446: la pasarela ensenaria el importe. Su pedido va con la forma de pago
            // habitual de su ficha, asi que no hay cobro por adelantado que arrancar.
            PedidosClienteController controller = ControllerConIdentidad(
                new Claim("cliente", "15191"), new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, "SinPrecios"));
            PedidoClienteRequest peticion = PeticionValida();
            peticion.TarjetaId = 7;

            var resultado = await controller.PostPagoCarrito(peticion);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostPedidoCliente_UsuarioSinPreciosConCobroDelCarrito_NoCreaElPedido()
        {
            // A este usuario no se le arranca el cobro del carrito, asi que si llega uno es que
            // algo no cuadra: no se le crea un pedido con una tarjeta que su politica no permite.
            PedidosClienteController controller = ControllerConIdentidad(
                new Claim("cliente", "15191"), new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, "SinPrecios"));
            PedidoClienteRequest peticion = PeticionValida();
            peticion.IdPagoCarrito = 689;

            var resultado = await controller.PostPedidoCliente(peticion);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        // TNV#69 / TNV#70: el paso de confirmacion — a donde se lo mandamos, o donde lo recoge.

        [TestMethod]
        public async Task GetDireccionesCliente_SinTokenDeCliente_NoAutorizado()
        {
            // Las direcciones de un cliente son suyas: quien no es ese cliente no las ve.
            PedidosClienteController controller = ControllerConIdentidad();

            var resultado = await controller.GetDireccionesCliente();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public void GetTiendasRecogida_SinTokenDeCliente_NoAutorizado()
        {
            PedidosClienteController controller = ControllerConIdentidad();

            var resultado = controller.GetTiendasRecogida();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public void GetTiendasRecogida_DevuelveLasTresTiendas()
        {
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));

            var resultado = controller.GetTiendasRecogida() as OkNegotiatedContentResult<List<TiendaRecogidaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(3, resultado.Content.Count);
            CollectionAssert.AreEquivalent(
                new[] { "ALG", "ALC", "REI" },
                resultado.Content.Select(t => t.Almacen).ToArray());
        }

        [TestMethod]
        public async Task PostPedidoCliente_UnaTiendaQueNoEsNuestra_NoCreaElPedido()
        {
            // El almacen de las lineas sale de aqui. Sin esta puerta, el cliente podria colocar su
            // pedido en el almacen ficticio de cualquiera mandando su codigo.
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));
            PedidoClienteRequest peticion = PeticionValida();
            peticion.TiendaRecogida = "CNG";

            var resultado = await controller.PostPedidoCliente(peticion);

            var badRequest = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequest);
            StringAssert.Contains(badRequest.Message, "tienda");
        }

        [TestMethod]
        public async Task PostPortesCliente_UnaTiendaQueNoEsNuestra_TampocoCalculaPortes()
        {
            // El mismo camino: si el pedido no se puede hacer asi, tampoco se le dice lo que
            // costaria.
            PedidosClienteController controller = ControllerConIdentidad(new Claim("cliente", "15191"));
            PedidoClienteRequest peticion = PeticionValida();
            peticion.TiendaRecogida = "XXX";

            var resultado = await controller.PostPortesCliente(peticion);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void CobrarCarritoAntesDelPedido_SinTocarElWebConfig_VieneEncendido()
        {
            // Nace encendido porque es el orden correcto; el interruptor esta para poder volver
            // al antiguo sin publicar una version de la app.
            Assert.IsTrue(PedidosClienteController.CobrarCarritoAntesDelPedido);
        }
    }
}

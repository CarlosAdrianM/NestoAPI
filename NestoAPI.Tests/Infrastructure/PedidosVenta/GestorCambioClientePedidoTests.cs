using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#519: el cambio de cliente de principio a fin con la BD falsa: qué se rechaza sin tocar nada,
    /// qué se recalcula (IVA de las líneas, precios, cabecera) y que el guardado final pasa por el PUT.
    /// </summary>
    [TestClass]
    public class GestorCambioClientePedidoTests
    {
        private const string EMPRESA = "1";
        private const int NUMERO = 926000;

        private NVEntities db;
        private CabPedidoVta cab;
        private List<Cliente> clientes;
        private List<CondPagoCliente> condiciones;
        private List<LinPedidoVta> lineas;
        private List<Prepago> prepagos;
        private List<EnviosAgencia> envios;
        private List<PedidoVentaDTO> guardados;
        private List<Tuple<int, string>> importesCalculados;
        private List<Modificacion> modificaciones;
        private RespuestaValidacion validacion;
        private GestorCambioClientePedido gestor;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 1, Nº_Cliente = "10000", Contacto = "0", Producto = "AA11", TipoLinea = 1, Estado = 1, Cantidad = 2, Precio = 10, IVA = "G21" },
                new LinPedidoVta { Nº_Orden = 2, Nº_Cliente = "10000", Contacto = "0", Producto = "BB22", TipoLinea = 1, Estado = 1, Cantidad = 1, Precio = 5, IVA = "G21", NºOferta = 4 },
                new LinPedidoVta { Nº_Orden = 3, Nº_Cliente = "10000", Contacto = "0", Producto = null, TipoLinea = 0, Estado = 1, Texto = "Comentario" }
            };
            cab = new CabPedidoVta
            {
                Empresa = EMPRESA,
                Número = NUMERO,
                Nº_Cliente = "10000",
                Contacto = "0",
                IVA = "G21",
                Forma_Pago = "EFC",
                PlazosPago = "CONTADO",
                Ruta = "FW ",
                Vendedor = "NV ",
                LinPedidoVtas = lineas
            };
            clientes = new List<Cliente>
            {
                new Cliente { Empresa = EMPRESA, Nº_Cliente = "10000", Contacto = "0", ClientePrincipal = true, Estado = 0, CIF_NIF = "B1", IVA = "G21" },
                new Cliente { Empresa = EMPRESA, Nº_Cliente = "20000", Contacto = "0", ClientePrincipal = true, Estado = 0, CIF_NIF = "B2", IVA = "R52", Vendedor = "JE ", Ruta = "AT ", PeriodoFacturación = "NRM" }
            };
            condiciones = new List<CondPagoCliente>
            {
                new CondPagoCliente { Empresa = EMPRESA, Nº_Cliente = "20000", Contacto = "0", FormaPago = "TRN", PlazosPago = "30D", ImporteMínimo = 0 }
            };
            prepagos = new List<Prepago>();
            envios = new List<EnviosAgencia>();
            modificaciones = new List<Modificacion>();

            Configurar(s => A.CallTo(() => db.CabPedidoVtas).Returns(s), new List<CabPedidoVta> { cab });
            Configurar(s => A.CallTo(() => db.Clientes).Returns(s), clientes);
            Configurar(s => A.CallTo(() => db.CondPagoClientes).Returns(s), condiciones);
            Configurar(s => A.CallTo(() => db.Prepagos).Returns(s), prepagos);
            Configurar(s => A.CallTo(() => db.EnviosAgencias).Returns(s), envios);
            Configurar(s => A.CallTo(() => db.EfectosPedidosVentas).Returns(s), new List<EfectoPedidoVenta>());
            Configurar(s => A.CallTo(() => db.PagosTPV).Returns(s), new List<PagoTPV>());
            Configurar(s => A.CallTo(() => db.FormasPago).Returns(s), new List<FormaPago> { new FormaPago { Empresa = EMPRESA, Número = "TRN", CCCObligatorio = false } });
            Configurar(s => A.CallTo(() => db.PlazosPago).Returns(s), new List<PlazoPago> { new PlazoPago { Empresa = EMPRESA, Número = "30D", DtoProntoPago = 0.02M } });
            Configurar(s => A.CallTo(() => db.DescuentosClientes).Returns(s), new List<DescuentosCliente>());
            Configurar(s => A.CallTo(() => db.Productos).Returns(s), new List<Producto>());
            Configurar(s => A.CallTo(() => db.VendedoresPedidosGruposProductos).Returns(s), new List<VendedorPedidoGrupoProducto>());
            Configurar(s => A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(s), new List<VendedorClienteGrupoProducto>());
            DbSet<Modificacion> fakeModificaciones = Configurar(s => A.CallTo(() => db.Modificaciones).Returns(s), modificaciones);
            A.CallTo(() => fakeModificaciones.Add(A<Modificacion>._)).ReturnsLazily((Modificacion m) => { modificaciones.Add(m); return m; });

            guardados = new List<PedidoVentaDTO>();
            importesCalculados = new List<Tuple<int, string>>();
            validacion = new RespuestaValidacion { ValidacionSuperada = true };
            gestor = new GestorCambioClientePedido(db, p => { guardados.Add(p); return Task.FromResult<IHttpActionResult>(new OkResult(new FakeController())); })
            {
                UsarTransaccion = false,
                LeerPedido = (e, n) => Task.FromResult(ComoDto(cab)),
                CalcularPrecio = (e, producto, cliente, contacto, cantidad) =>
                    Task.FromResult(new ProductoPlantillaDTO { precio = 9, descuento = 0, aplicarDescuento = true }),
                CalcularImportes = (linea, iva) => importesCalculados.Add(Tuple.Create(linea.Nº_Orden, iva)),
                Validar = p => validacion,
                RegistrarEnElmah = _ => { }
            };
        }

        [TestMethod]
        public async Task CambiarCliente_PedidoQueNoExiste_NoEncontrado()
        {
            ResultadoCambioClientePedido resultado = await gestor.CambiarCliente(EMPRESA, 1, new CambiarClientePedidoRequest { Cliente = "20000" }, "u");

            Assert.IsTrue(resultado.NoEncontrado);
        }

        [TestMethod]
        public async Task CambiarCliente_ConPicking_RechazaSinGuardarNada()
        {
            lineas[0].Picking = 77;

            ResultadoCambioClientePedido resultado = await gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000" }, "u");

            StringAssert.Contains(resultado.Error, "picking");
            Assert.AreEqual("10000", cab.Nº_Cliente);
            Assert.AreEqual("10000", lineas[0].Nº_Cliente);
            Assert.AreEqual(0, guardados.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CambiarCliente_ConEnvioDeAgencia_Rechaza()
        {
            envios.Add(new EnviosAgencia { Empresa = EMPRESA, Pedido = NUMERO, Numero = 248944, Estado = -1 });

            ResultadoCambioClientePedido resultado = await gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000" }, "u");

            StringAssert.Contains(resultado.Error, "248944");
            Assert.AreEqual(0, guardados.Count);
        }

        [TestMethod]
        public async Task CambiarCliente_ConPrepagoVivo_Rechaza()
        {
            prepagos.Add(new Prepago { Empresa = EMPRESA, Pedido = NUMERO, Importe = 10, Factura = null });

            ResultadoCambioClientePedido resultado = await gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000" }, "u");

            StringAssert.Contains(resultado.Error, "prepago");
        }

        [TestMethod]
        public async Task CambiarCliente_AlMismoCliente_Rechaza()
        {
            ResultadoCambioClientePedido resultado = await gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "10000", Contacto = "0" }, "u");

            StringAssert.Contains(resultado.Error, "ya es del cliente");
        }

        [TestMethod]
        public async Task CambiarCliente_FichaSinCondicionesDePago_Rechaza()
        {
            condiciones.Clear();

            ResultadoCambioClientePedido resultado = await gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000" }, "u");

            StringAssert.Contains(resultado.Error, "condiciones de pago");
            Assert.AreEqual("10000", cab.Nº_Cliente);
        }

        [TestMethod]
        public async Task CambiarCliente_SinPicking_CambiaYRecalculaTodoConElClienteNuevo()
        {
            ResultadoCambioClientePedido resultado = await gestor.CambiarCliente(EMPRESA, NUMERO,
                new CambiarClientePedidoRequest { Cliente = "20000" }, "NUEVAVISION\\lidia");

            Assert.IsNull(resultado.Error);
            // Cabecera
            Assert.AreEqual("20000", cab.Nº_Cliente);
            Assert.AreEqual("0", cab.Contacto);
            Assert.AreEqual("TRN", cab.Forma_Pago);
            Assert.AreEqual("30D", cab.PlazosPago);
            Assert.AreEqual("JE ", cab.Vendedor);
            Assert.AreEqual("AT ", cab.Ruta);
            // Líneas: todas al cliente nuevo, pronto pago de sus plazos
            Assert.IsTrue(lineas.All(l => l.Nº_Cliente == "20000"));
            Assert.IsTrue(lineas.All(l => l.DescuentoPP == 0.02M));
            // El precio se recalcula en la normal y no en la de oferta
            Assert.AreEqual(9M, lineas[0].Precio);
            Assert.AreEqual(5M, lineas[1].Precio);
            // Se guarda por el PUT y queda rastro
            Assert.AreEqual(1, guardados.Count);
            Assert.AreEqual("NUEVAVISION\\lidia", guardados[0].Usuario);
            Assert.AreEqual(1, modificaciones.Count);
            StringAssert.Contains(modificaciones[0].Anterior, "10000");
            StringAssert.Contains(modificaciones[0].Nuevo, "CambiarCliente");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedTwiceExactly();
            Assert.AreEqual("10000", resultado.Respuesta.ClienteAnterior);
            Assert.AreEqual("20000", resultado.Respuesta.Cliente);
        }

        [TestMethod]
        public async Task CambiarCliente_DeIvaGeneralARecargo_RecalculaLasLineasConElIvaNuevo()
        {
            _ = await gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000" }, "u");

            Assert.AreEqual("R52", cab.IVA);
            // Las dos de producto con el IVA nuevo; la de texto no lleva importes
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, importesCalculados.Select(i => i.Item1).ToArray());
            Assert.IsTrue(importesCalculados.All(i => i.Item2 == "R52"));
        }

        [TestMethod]
        public async Task CambiarCliente_DeSinIvaAConIva_LasLineasSinCodigoTomanElDelProducto()
        {
            cab.IVA = null;
            lineas[0].IVA = null;
            Configurar(s => A.CallTo(() => db.Productos).Returns(s), new List<Producto> { new Producto { Empresa = EMPRESA, Número = "AA11", IVA_Repercutido = "G21" } });

            _ = await gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000" }, "u");

            Assert.AreEqual("G21", lineas[0].IVA);
        }

        [TestMethod]
        public async Task CambiarCliente_NoPasaLaValidacionConElClienteNuevo_LanzaYNoLlegaAlPut()
        {
            validacion = new RespuestaValidacion { ValidacionSuperada = false, Motivo = "Oferta no permitida para el cliente 20000" };

            PedidoValidacionException excepcion = await AssertLanza<PedidoValidacionException>(() =>
                gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000" }, "u"));

            StringAssert.Contains(excepcion.Message, "Oferta no permitida");
            Assert.AreEqual(0, guardados.Count);
        }

        [TestMethod]
        public async Task CambiarCliente_NoPasaLaValidacionPeroQuienPuedeLoFuerza_Sigue()
        {
            validacion = new RespuestaValidacion { ValidacionSuperada = false, Motivo = "Oferta no permitida" };
            gestor.PuedeOmitirValidacion = _ => true;

            ResultadoCambioClientePedido resultado = await gestor.CambiarCliente(EMPRESA, NUMERO,
                new CambiarClientePedidoRequest { Cliente = "20000", CreadoSinPasarValidacion = true }, "u");

            Assert.IsNull(resultado.Error);
            Assert.AreEqual(1, guardados.Count);
            Assert.IsTrue(guardados[0].CreadoSinPasarValidacion);
        }

        [TestMethod]
        public async Task CambiarCliente_ForzarSinPermiso_NoSirve()
        {
            validacion = new RespuestaValidacion { ValidacionSuperada = false, Motivo = "Oferta no permitida" };

            _ = await AssertLanza<PedidoValidacionException>(() =>
                gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000", CreadoSinPasarValidacion = true }, "u"));
            Assert.AreEqual(0, guardados.Count);
        }

        [TestMethod]
        public async Task CambiarCliente_ElPutRechazaElGuardado_Lanza()
        {
            gestor.GuardarPedido = p => Task.FromResult<IHttpActionResult>(new BadRequestErrorMessageResult("No se pueden mezclar pedidos con presupuestos", new FakeController()));

            NestoBusinessException excepcion = await AssertLanza<NestoBusinessException>(() =>
                gestor.CambiarCliente(EMPRESA, NUMERO, new CambiarClientePedidoRequest { Cliente = "20000" }, "u"));

            StringAssert.Contains(excepcion.Message, "mezclar");
        }

        #region Utilidades

        private static PedidoVentaDTO ComoDto(CabPedidoVta cabecera)
        {
            PedidoVentaDTO dto = new PedidoVentaDTO
            {
                empresa = cabecera.Empresa,
                numero = cabecera.Número,
                cliente = cabecera.Nº_Cliente?.Trim(),
                contacto = cabecera.Contacto?.Trim(),
                formaPago = cabecera.Forma_Pago,
                plazosPago = cabecera.PlazosPago,
                iva = cabecera.IVA
            };
            foreach (LinPedidoVta l in cabecera.LinPedidoVtas)
            {
                dto.Lineas.Add(new LineaPedidoVentaDTO { id = l.Nº_Orden, Producto = l.Producto, Cantidad = l.Cantidad ?? 0, PrecioUnitario = l.Precio ?? 0, tipoLinea = l.TipoLinea });
            }
            return dto;
        }

        private static async Task<T> AssertLanza<T>(Func<Task> accion) where T : Exception
        {
            try
            {
                await accion();
            }
            catch (T excepcion)
            {
                return excepcion;
            }
            Assert.Fail($"Se esperaba {typeof(T).Name}");
            return null;
        }

        private static DbSet<T> Configurar<T>(Action<DbSet<T>> asignar, List<T> datos) where T : class
        {
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.AsQueryable().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => ((DbQuery<T>)fakeSet).Include(A<string>._)).Returns((DbQuery<T>)fakeSet);
            asignar(fakeSet);
            return fakeSet;
        }

        private class FakeController : ApiController { }

        #endregion
    }
}

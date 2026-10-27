using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using M = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#704 (caso 929152): registrar si el modo de servicio de un pedido lo eligió el vendedor o venía sugerido. Best-effort,
    /// en segundo plano, sin interruptor y sin tocar nada del pedido; sin la tabla, un aviso a ELMAH por proceso.
    /// </summary>
    [TestClass]
    public class RegistroOrigenModoServicioTests
    {
        private Func<IRepositorioOrigenModoServicio> crearRepositorioOriginal;
        private Action<Action> lanzadorOriginal;
        private Action<Exception> registradorOriginal;
        private Func<DateTime> ahoraOriginal;

        private IRepositorioOrigenModoServicio repositorio;
        private List<FilaOrigenModoServicio> guardadas;
        private List<Exception> avisos;
        private static readonly DateTime AHORA = new DateTime(2026, 10, 22, 15, 34, 40);

        [TestInitialize]
        public void Setup()
        {
            crearRepositorioOriginal = RegistroOrigenModoServicio.CrearRepositorio;
            lanzadorOriginal = RegistroOrigenModoServicio.Lanzador;
            registradorOriginal = RegistroOrigenModoServicio.Registrador;
            ahoraOriginal = RegistroOrigenModoServicio.Ahora;

            repositorio = A.Fake<IRepositorioOrigenModoServicio>();
            guardadas = new List<FilaOrigenModoServicio>();
            avisos = new List<Exception>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.Guardar(A<FilaOrigenModoServicio>._)).Invokes(call => guardadas.Add(call.GetArgument<FilaOrigenModoServicio>(0)));

            RegistroOrigenModoServicio.ReiniciarAvisos();
            RegistroOrigenModoServicio.CrearRepositorio = () => repositorio;
            RegistroOrigenModoServicio.Lanzador = trabajo => trabajo(); // síncrono para poder comprobarlo
            RegistroOrigenModoServicio.Registrador = ex => avisos.Add(ex);
            RegistroOrigenModoServicio.Ahora = () => AHORA;
        }

        [TestCleanup]
        public void Cleanup()
        {
            RegistroOrigenModoServicio.CrearRepositorio = crearRepositorioOriginal;
            RegistroOrigenModoServicio.Lanzador = lanzadorOriginal;
            RegistroOrigenModoServicio.Registrador = registradorOriginal;
            RegistroOrigenModoServicio.Ahora = ahoraOriginal;
            RegistroOrigenModoServicio.ReiniciarAvisos();
        }

        /// <summary>El 929152: 3 líneas verdes y una roja, la app mandó modo 4 (el sugerido) sin tocarlo.</summary>
        private static PedidoVentaDTO Pedido929152(byte? modo = M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, string origen = OrigenesModoServicio.SUGERIDO) => new PedidoVentaDTO
        {
            empresa = "1",
            numero = 929152,
            cliente = "29298",
            contacto = "0",
            Usuario = "NUEVAVISION\\David",
            modoServicio = modo,
            servirJunto = modo.HasValue && M.EsTodoJunto(modo.Value),
            OrigenModoServicio = origen,
            ModoServicioSugerido = M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ,
            Lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { id = 1, Producto = "VERDE1", Cantidad = 1, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO },
                new LineaPedidoVentaDTO { id = 2, Producto = "VERDE2", Cantidad = 1, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO },
                new LineaPedidoVentaDTO { id = 3, Producto = "VERDE3", Cantidad = 1, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO },
                new LineaPedidoVentaDTO { id = 4, Producto = "ROJO45979", Cantidad = 1, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO }
            }
        };

        private static SugeridorModoServicio.Sugerencia Sugerencia(byte modo, params byte[] permitidos) => new SugeridorModoServicio.Sugerencia
        {
            Modo = modo,
            Motivo = "3 líneas con stock y 1 sin stock.",
            ModosPermitidos = permitidos.ToList()
        };

        // ================= Las reglas puras =================

        [TestMethod]
        public void Origenes_Normalizar_CanonicoSinCajaNiEspacios_YNullParaElClienteAntiguo()
        {
            Assert.AreEqual("Usuario", OrigenesModoServicio.Normalizar(" usuario "));
            Assert.AreEqual("Sugerido", OrigenesModoServicio.Normalizar("SUGERIDO"));
            Assert.AreEqual("PorDefecto", OrigenesModoServicio.Normalizar("pordefecto"));
            Assert.AreEqual("Corregido", OrigenesModoServicio.Normalizar("Corregido"));
            Assert.IsNull(OrigenesModoServicio.Normalizar(null));
            Assert.IsNull(OrigenesModoServicio.Normalizar("   "));
        }

        [TestMethod]
        public void Origenes_UnValorDesconocido_SeGuardaTalCualRecortado_ParaVerQueMandaElCliente()
        {
            Assert.AreEqual("Automatico", OrigenesModoServicio.Normalizar("Automatico"));
            Assert.IsFalse(OrigenesModoServicio.EsConocido("Automatico"));
            Assert.IsTrue(OrigenesModoServicio.EsConocido("usuario"));
            Assert.AreEqual(OrigenesModoServicio.LARGO_MAXIMO, OrigenesModoServicio.Normalizar(new string('x', 50)).Length);
        }

        [TestMethod]
        public void Capturar_CopiaLoQueMandaElClienteAntesDeNormalizar()
        {
            PedidoVentaDTO pedido = Pedido929152(modo: M.TODO_JUNTO, origen: "usuario");
            pedido.servirJunto = true;

            EnvioModoServicio envio = RegistroOrigenModoServicio.Capturar(pedido);
            // Lo que haría NormalizarAlCrear o una corrección de #518 después:
            pedido.modoServicio = M.SEGUN_VAYA_ENTRANDO;
            pedido.servirJunto = false;

            Assert.AreEqual(M.TODO_JUNTO, envio.ModoEnviado);
            Assert.IsTrue(envio.ServirJuntoEnviado);
            Assert.AreEqual(OrigenesModoServicio.USUARIO, envio.Origen);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, envio.ModoSugeridoCliente);
        }

        [TestMethod]
        public void Capturar_ClienteAntiguoSinModoNiOrigen_TodoNull_YNuncaLanza()
        {
            EnvioModoServicio envio = RegistroOrigenModoServicio.Capturar(new PedidoVentaDTO());
            EnvioModoServicio nulo = RegistroOrigenModoServicio.Capturar(null);

            Assert.IsNull(envio.ModoEnviado);
            Assert.IsNull(envio.Origen);
            Assert.IsNull(envio.ModoSugeridoCliente);
            Assert.IsNotNull(nulo);
        }

        [TestMethod]
        public void Construir_AlCrear_ElCaso929152_SugeridoYSinTocar()
        {
            PedidoVentaDTO pedido = Pedido929152();
            EnvioModoServicio envio = RegistroOrigenModoServicio.Capturar(pedido);

            FilaOrigenModoServicio fila = RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, envio,
                Sugerencia(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, M.TODO_JUNTO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, M.SEGUN_VAYA_ENTRANDO),
                null, "NestoApp", "David", AHORA);

            Assert.AreEqual(AHORA, fila.Fecha);
            Assert.AreEqual("Crear", fila.Operacion);
            Assert.AreEqual("1", fila.Empresa);
            Assert.AreEqual(929152, fila.Pedido);
            Assert.AreEqual("29298", fila.Cliente);
            Assert.AreEqual("0", fila.Contacto);
            Assert.AreEqual("NUEVAVISION\\David", fila.Usuario);
            Assert.AreEqual("NestoApp", fila.Aplicacion);
            Assert.AreEqual("Sugerido", fila.Origen);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, fila.ModoEnviado);
            Assert.IsFalse(fila.ServirJuntoEnviado);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, fila.ModoSugeridoCliente);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, fila.ModoGuardado);
            Assert.IsNull(fila.ModoAnterior);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, fila.ModoSugeridoServidor);
            Assert.AreEqual("1,2,4", fila.PermitidosServidor, "ordenados y sin repetir");
            Assert.AreEqual("3 líneas con stock y 1 sin stock.", fila.MotivoServidor);
            Assert.IsTrue(fila.CoincideConElEnviado);
        }

        [TestMethod]
        public void Construir_AlCrear_ElServidorCorrigioElModo_SeVeEnLaFila()
        {
            // El vendedor eligió «Todo junto» (1) y la API lo corrigió a 2 (solo en tienda, #518); la fila lo deja ver.
            PedidoVentaDTO pedido = Pedido929152(modo: M.TODO_JUNTO, origen: OrigenesModoServicio.USUARIO);
            EnvioModoServicio envio = RegistroOrigenModoServicio.Capturar(pedido);
            pedido.modoServicio = M.SEGUN_VAYA_ENTRANDO;
            pedido.servirJunto = false;

            FilaOrigenModoServicio fila = RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, envio,
                Sugerencia(M.SEGUN_VAYA_ENTRANDO, M.SEGUN_VAYA_ENTRANDO), null, null, "NUEVAVISION\\Reina", AHORA);

            Assert.AreEqual("Usuario", fila.Origen);
            Assert.AreEqual(M.TODO_JUNTO, fila.ModoEnviado);
            Assert.AreEqual(M.SEGUN_VAYA_ENTRANDO, fila.ModoGuardado);
            Assert.AreEqual("2", fila.PermitidosServidor);
            Assert.IsFalse(fila.CoincideConElEnviado);
        }

        [TestMethod]
        public void Construir_ClienteAntiguoSinModo_OrigenNullYModoEnviadoNull_PeroSeRegistraElGuardado()
        {
            PedidoVentaDTO pedido = Pedido929152(modo: null, origen: null);
            pedido.ModoServicioSugerido = null;
            EnvioModoServicio envio = RegistroOrigenModoServicio.Capturar(pedido);
            pedido.modoServicio = M.TRAS_REPONER_DE_TIENDAS; // nació en el sugerido

            FilaOrigenModoServicio fila = RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, envio,
                Sugerencia(M.TRAS_REPONER_DE_TIENDAS, M.TRAS_REPONER_DE_TIENDAS, M.TODO_JUNTO), null, null, null, AHORA);

            Assert.IsNull(fila.Origen);
            Assert.IsNull(fila.ModoEnviado);
            Assert.IsNull(fila.ModoSugeridoCliente);
            Assert.AreEqual(M.TRAS_REPONER_DE_TIENDAS, fila.ModoGuardado);
            Assert.AreEqual(M.TRAS_REPONER_DE_TIENDAS, fila.ModoSugeridoServidor);
            Assert.IsTrue(fila.CoincideConElEnviado, "Sin modo enviado no hay nada que contradecir");
        }

        [TestMethod]
        public void Construir_SinSugerenciaDelServidor_LasColumnasDelServidorVanANull()
        {
            FilaOrigenModoServicio fila = RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_CREAR, Pedido929152(), null, null, null, null, null, AHORA);

            Assert.IsNull(fila.ModoSugeridoServidor);
            Assert.IsNull(fila.PermitidosServidor);
            Assert.IsNull(fila.MotivoServidor);
            Assert.AreEqual("Sugerido", fila.Origen, "Sin envío capturado, se captura del pedido");
            Assert.AreEqual("Nesto", fila.Aplicacion, "Sin cabecera X-Aplicacion se infiere del usuario de dominio");
        }

        [TestMethod]
        public void Construir_AlModificar_SoloSiCambiaElModoEfectivo()
        {
            PedidoVentaDTO pedido = Pedido929152(modo: M.TODO_JUNTO, origen: OrigenesModoServicio.USUARIO);
            pedido.servirJunto = true;

            FilaOrigenModoServicio sinCambio = RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_MODIFICAR, pedido,
                RegistroOrigenModoServicio.Capturar(pedido), null, M.TODO_JUNTO, "Nesto", null, AHORA);
            FilaOrigenModoServicio conCambio = RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_MODIFICAR, pedido,
                RegistroOrigenModoServicio.Capturar(pedido), null, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, "Nesto", null, AHORA);

            Assert.IsNull(sinCambio, "El modo no cambia: nada que explicar");
            Assert.IsNotNull(conCambio);
            Assert.AreEqual("Modificar", conCambio.Operacion);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, conCambio.ModoAnterior);
            Assert.AreEqual(M.TODO_JUNTO, conCambio.ModoGuardado);
            Assert.AreEqual("Usuario", conCambio.Origen);
            Assert.AreEqual("Nesto", conCambio.Aplicacion);
        }

        [TestMethod]
        public void Construir_SinPedidoGrabado_Null()
        {
            Assert.IsNull(RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_CREAR, null, null, null, null, null, null, AHORA));
            Assert.IsNull(RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_CREAR, new PedidoVentaDTO { numero = 0 }, null, null, null, null, null, AHORA));
        }

        [TestMethod]
        public void Lista_OrdenadaSinRepetidos_YVaciaSinModos()
        {
            Assert.AreEqual("1,2,4", RegistroOrigenModoServicio.Lista(new byte[] { 4, 1, 2, 1 }));
            Assert.AreEqual(string.Empty, RegistroOrigenModoServicio.Lista(null));
        }

        // ================= Registrar: en segundo plano y best-effort =================

        [TestMethod]
        public void Registrar_AlCrear_GuardaUnaFila()
        {
            PedidoVentaDTO pedido = Pedido929152();

            RegistroOrigenModoServicio.Registrar(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, RegistroOrigenModoServicio.Capturar(pedido),
                Sugerencia(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, M.TODO_JUNTO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ), null, "NestoApp", "David");

            Assert.AreEqual(1, guardadas.Count);
            Assert.AreEqual(929152, guardadas[0].Pedido);
            Assert.AreEqual("Sugerido", guardadas[0].Origen);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void Registrar_AlModificarSinCambioDeModo_NiLanzaElTrabajo()
        {
            int lanzados = 0;
            RegistroOrigenModoServicio.Lanzador = trabajo => { lanzados++; trabajo(); };
            PedidoVentaDTO pedido = Pedido929152();

            RegistroOrigenModoServicio.Registrar(RegistroOrigenModoServicio.OPERACION_MODIFICAR, pedido, RegistroOrigenModoServicio.Capturar(pedido), null,
                M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, null, null);

            Assert.AreEqual(0, lanzados);
            Assert.AreEqual(0, guardadas.Count);
        }

        [TestMethod]
        public void Registrar_SinLaTabla_NoGuarda_AvisaUnaVezPorProceso_YElPedidoSigue()
        {
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);
            PedidoVentaDTO pedido = Pedido929152();

            RegistroOrigenModoServicio.Registrar(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, RegistroOrigenModoServicio.Capturar(pedido), null, null, null, null);
            RegistroOrigenModoServicio.Registrar(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, RegistroOrigenModoServicio.Capturar(pedido), null, null, null, null);

            A.CallTo(() => repositorio.Guardar(A<FilaOrigenModoServicio>._)).MustNotHaveHappened();
            Assert.AreEqual(1, avisos.Count, "Un aviso por proceso, no uno por pedido");
            StringAssert.Contains(avisos[0].Message, "Issue704_PedidosModoServicioOrigen.sql");
        }

        [TestMethod]
        public void Registrar_FallaElInsert_SeTragaElErrorYAvisaUnaVez()
        {
            A.CallTo(() => repositorio.Guardar(A<FilaOrigenModoServicio>._)).Throws(new InvalidOperationException("Invalid column name 'MotivoServidor'"));
            PedidoVentaDTO pedido = Pedido929152();

            RegistroOrigenModoServicio.Registrar(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, RegistroOrigenModoServicio.Capturar(pedido), null, null, null, null);
            RegistroOrigenModoServicio.Registrar(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, RegistroOrigenModoServicio.Capturar(pedido), null, null, null, null);

            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "929152");
            StringAssert.Contains(avisos[0].Message, "MotivoServidor");
        }

        [TestMethod]
        public void Registrar_FallaAlCrearElRepositorio_NuncaLanza()
        {
            RegistroOrigenModoServicio.CrearRepositorio = () => throw new InvalidOperationException("Falta la cadena de conexión NestoConnection");
            PedidoVentaDTO pedido = Pedido929152();

            RegistroOrigenModoServicio.Registrar(RegistroOrigenModoServicio.OPERACION_CREAR, pedido, RegistroOrigenModoServicio.Capturar(pedido), null, null, null, null);

            Assert.AreEqual(1, avisos.Count);
        }

        [TestMethod]
        public void Procesar_SinFila_NoHaceNada()
        {
            Assert.IsFalse(RegistroOrigenModoServicio.Procesar(null));
            A.CallTo(() => repositorio.TablaExiste()).MustNotHaveHappened();
        }

        // ================= El controlador: la sugerencia del servidor al crear =================

        [TestMethod]
        public void Controlador_ModoServicioAlCrear_DejaLaSugerenciaParaRegistrarla()
        {
            IGestorStocks stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.ColorStock(A<string>.That.StartsWith("VERDE"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.VERDE);
            A.CallTo(() => stocks.ColorStock(A<string>.That.StartsWith("ROJO"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.ROJO);
            var controller = new PedidosVentaController(A.Fake<NVEntities>())
            {
                LectorParametros = A.Fake<ILectorParametrosUsuario>(),
                Stocks = () => stocks
            };
            PedidoVentaDTO pedido = Pedido929152(modo: null, origen: null);

            byte modo = controller.ModoServicioAlCrear(pedido);

            Assert.IsNotNull(controller.SugerenciaAlCrear);
            Assert.AreEqual(modo, controller.SugerenciaAlCrear.Modo);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, modo, "3 verdes + 1 roja: lo del 929152");
            Assert.IsTrue(controller.SugerenciaAlCrear.ModosPermitidos.Contains(M.TODO_JUNTO), "El 1 estaba permitido");

            FilaOrigenModoServicio fila = RegistroOrigenModoServicio.Construir(RegistroOrigenModoServicio.OPERACION_CREAR,
                Pedido929152(), RegistroOrigenModoServicio.Capturar(Pedido929152()), controller.SugerenciaAlCrear, null, "NestoApp", null, AHORA);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, fila.ModoSugeridoServidor);
            StringAssert.Contains(fila.PermitidosServidor, "1");
            StringAssert.Contains(fila.PermitidosServidor, "4");
        }

        [TestMethod]
        public void Controlador_ModoServicioAlCrear_SiFallaElStock_SugerenciaNull_YElDefectoComoSiempre()
        {
            IGestorStocks stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._, A<int>._)).Throws(new InvalidOperationException("sin BD"));
            var controller = new PedidosVentaController(A.Fake<NVEntities>())
            {
                LectorParametros = A.Fake<ILectorParametrosUsuario>(),
                Stocks = () => stocks
            };

            byte modo = controller.ModoServicioAlCrear(Pedido929152(modo: null, origen: null));

            Assert.AreEqual(M.POR_DEFECTO, modo);
            Assert.IsNull(controller.SugerenciaAlCrear);
        }
    }
}

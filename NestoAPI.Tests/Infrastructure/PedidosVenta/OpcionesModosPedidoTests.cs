using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using F = NestoAPI.Models.Constantes.Pedidos.ModosFacturacion;
using S = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#688 (corte 1): LA regla servicio × facturación × casilla «¿Servir lo pendiente en cuanto haya stock?».
    /// </summary>
    [TestClass]
    public class OpcionesModosPedidoTests
    {
        private static readonly byte[] SERVICIOS = { S.TODO_JUNTO, S.SEGUN_VAYA_ENTRANDO, S.TRAS_REPONER_DE_TIENDAS, S.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ };
        private static readonly byte[] FACTURACIONES = { F.POR_ENTREGAS, F.AL_COMPLETAR, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES };
        private static readonly bool?[] INFORMADOS = { null, true, false };

        private static OpcionesModosPedidoDTO Calcular(byte servicio, byte facturacion, bool? informado = null, bool? almacenado = null, bool nota = false)
            => OpcionesModosPedido.Calcular(new SugeridorModoServicio.Sugerencia { Modo = servicio },
                new ModosFacturacionPermitidos.Sugerencia { Modo = facturacion, ModosPermitidos = new List<byte> { 1, 2, 3 } },
                servicio, facturacion, informado, almacenado, nota);

        [TestMethod]
        public void Matriz_Completa_ServicioPorFacturacionPorCasilla()
        {
            foreach (byte servicio in SERVICIOS)
            {
                foreach (byte facturacion in FACTURACIONES)
                {
                    foreach (bool? informado in INFORMADOS)
                    {
                        OpcionesModosPedidoDTO o = Calcular(servicio, facturacion, informado);
                        string caso = $"servicio {servicio}, facturación {facturacion}, casilla {informado?.ToString() ?? "null"}";
                        bool todoJunto = servicio == S.TODO_JUNTO;
                        bool todoAhora = facturacion == F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;

                        // Decisión 6 y 7: con Todo junto se oculta la facturación, salvo que ya sea Todo ahora (se permite y se ve).
                        Assert.AreEqual(!todoJunto || todoAhora, o.Facturacion.Visible, caso);
                        Assert.AreEqual(o.Facturacion.Visible, o.Facturacion.MotivoOculta == null, caso);
                        // Decisión 10: la casilla solo con Todo ahora (y no con Todo junto, que no deja nada pendiente).
                        Assert.AreEqual(todoAhora && !todoJunto, o.ServirPendienteAlHaberStock.Visible, caso);
                        Assert.AreEqual(o.ServirPendienteAlHaberStock.Visible, o.ServirPendienteAlHaberStock.Motivo == null, caso);
                        // Decisión 12: con Todo ahora, marcada salvo que se diga otra cosa. Sin Todo ahora, nunca.
                        Assert.AreEqual(todoAhora && (informado ?? true), o.ServirPendienteAlHaberStock.Valor, caso);
                        // Lo de siempre sale tal cual.
                        Assert.AreEqual(servicio, o.Servicio.Modo, caso);
                        Assert.AreEqual(facturacion, o.Facturacion.Modo, caso);
                    }
                }
            }
        }

        [TestMethod]
        public void TodoJunto_SinTodoAhora_OcultaLaFacturacionConMotivo()
        {
            OpcionesModosPedidoDTO o = Calcular(S.TODO_JUNTO, F.AL_COMPLETAR);

            Assert.IsFalse(o.Facturacion.Visible);
            Assert.AreEqual(OpcionesModosPedido.MOTIVO_FACTURACION_TODO_JUNTO, o.Facturacion.MotivoOculta);
            CollectionAssert.AreEqual(new List<byte> { 1, 2, 3 }, o.Facturacion.ModosPermitidos, "La sugerencia de siempre viaja entera");
        }

        [TestMethod]
        public void TodoJunto_ConTodoAhora_SeVeLaFacturacion_PeroNoLaCasilla()
        {
            OpcionesModosPedidoDTO o = Calcular(S.TODO_JUNTO, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES);

            Assert.IsTrue(o.Facturacion.Visible, "Decisión 7: se permite (con el aviso rojo de siempre) y hay que poder cambiarla");
            Assert.IsFalse(o.ServirPendienteAlHaberStock.Visible);
            Assert.AreEqual(OpcionesModosPedido.MOTIVO_CASILLA_TODO_JUNTO, o.ServirPendienteAlHaberStock.Motivo);
        }

        [TestMethod]
        public void SinTodoAhora_CasillaOcultaConMotivo_YFalse()
        {
            OpcionesModosPedidoDTO o = Calcular(S.SEGUN_VAYA_ENTRANDO, F.POR_ENTREGAS, informado: true);

            Assert.IsFalse(o.ServirPendienteAlHaberStock.Visible);
            Assert.IsFalse(o.ServirPendienteAlHaberStock.Valor);
            Assert.AreEqual(OpcionesModosPedido.MOTIVO_CASILLA_SOLO_TODO_AHORA, o.ServirPendienteAlHaberStock.Motivo);
        }

        [TestMethod]
        public void TodoAhora_SinInformar_MandaLoGuardado_YSiNoHayNada_Marcada()
        {
            Assert.IsFalse(Calcular(S.SEGUN_VAYA_ENTRANDO, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, informado: null, almacenado: false).ServirPendienteAlHaberStock.Valor);
            Assert.IsTrue(Calcular(S.SEGUN_VAYA_ENTRANDO, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, informado: null, almacenado: null).ServirPendienteAlHaberStock.Valor);
            Assert.IsTrue(Calcular(S.SEGUN_VAYA_ENTRANDO, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, informado: true, almacenado: false).ServirPendienteAlHaberStock.Valor,
                "Lo que manda el cliente, manda");
        }

        [TestMethod]
        public void NotaDeEntrega_SinFacturacion_ConLaCasillaDeLaNota()
        {
            OpcionesModosPedidoDTO o = Calcular(S.TODO_JUNTO, F.POR_ENTREGAS, almacenado: true, nota: true);

            Assert.IsFalse(o.Facturacion.Visible);
            Assert.AreEqual(ModosFacturacionPermitidos.MOTIVO_NOTA_ENTREGA, o.Facturacion.MotivoOculta);
            Assert.IsTrue(o.ServirPendienteAlHaberStock.Visible);
            Assert.IsTrue(o.ServirPendienteAlHaberStock.Valor);
            Assert.IsFalse(Calcular(S.TODO_JUNTO, F.POR_ENTREGAS, almacenado: null, nota: true).ServirPendienteAlHaberStock.Valor);
        }

        [TestMethod]
        public void Efectivo_SoloConTodoAhora_ONota_YNullEsSinMarcar()
        {
            Assert.IsTrue(OpcionesModosPedido.Efectivo(true, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, esNotaEntrega: false));
            Assert.IsFalse(OpcionesModosPedido.Efectivo(null, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, esNotaEntrega: false), "Decisión 13: los existentes, como hoy");
            Assert.IsFalse(OpcionesModosPedido.Efectivo(true, F.AL_COMPLETAR, esNotaEntrega: false));
            Assert.IsTrue(OpcionesModosPedido.Efectivo(true, F.POR_ENTREGAS, esNotaEntrega: true));
            Assert.IsFalse(OpcionesModosPedido.Efectivo(null, F.POR_ENTREGAS, esNotaEntrega: true));
        }

        // POST/PUT: rechazo solo al ENTRAR

        [TestMethod]
        public void ResolverAlGuardar_Matriz()
        {
            byte?[] anteriores = { null, F.POR_ENTREGAS, F.AL_COMPLETAR, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES };
            foreach (byte nuevo in FACTURACIONES)
            {
                foreach (byte? anterior in anteriores)
                {
                    foreach (bool? informado in INFORMADOS)
                    {
                        OpcionesModosPedido.ResolucionCasilla r = OpcionesModosPedido.ResolverAlGuardar(informado, nuevo, anterior);
                        string caso = $"nuevo {nuevo}, anterior {anterior?.ToString() ?? "alta"}, casilla {informado?.ToString() ?? "null"}";
                        bool nuevoTodoAhora = nuevo == F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;
                        bool anteriorTodoAhora = anterior == F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;

                        bool debeRechazar = !nuevoTodoAhora && informado == true && !anteriorTodoAhora;
                        Assert.AreEqual(debeRechazar, r.Error != null, caso);
                        bool? esperado = nuevoTodoAhora ? (informado ?? (anteriorTodoAhora ? (bool?)null : true)) : null;
                        Assert.AreEqual(esperado, r.AGuardar, caso);
                    }
                }
            }
        }

        [TestMethod]
        public void ResolverAlGuardar_AltaEnTodoAhoraSinInformar_Marcada()
        {
            Assert.AreEqual(true, OpcionesModosPedido.ResolverAlGuardar(null, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, null).AGuardar);
        }

        [TestMethod]
        public void ResolverAlGuardar_MarcarSinTodoAhora_SeRechazaConMensajeDeUsted()
        {
            OpcionesModosPedido.ResolucionCasilla r = OpcionesModosPedido.ResolverAlGuardar(true, F.AL_COMPLETAR, null);

            Assert.AreEqual(OpcionesModosPedido.MOTIVO_RECHAZO_CASILLA, r.Error);
            StringAssert.Contains(r.Error, "Desmárquela");
        }

        [TestMethod]
        public void ResolverAlGuardar_SaleDeTodoAhoraMandandoLaCasillaQueLeyo_NoSeRechaza_NiSeToca()
        {
            OpcionesModosPedido.ResolucionCasilla r = OpcionesModosPedido.ResolverAlGuardar(true, F.POR_ENTREGAS, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES);

            Assert.IsNull(r.Error);
            Assert.IsNull(r.AGuardar);
        }

        // El endpoint

        private static PedidosVentaController Controller(params CabPedidoVta[] cabeceras)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<CabPedidoVta> fakeCab = A.Fake<DbSet<CabPedidoVta>>(o => o.Implements(typeof(IQueryable<CabPedidoVta>)).Implements(typeof(IDbAsyncEnumerable<CabPedidoVta>)));
            ConfigurarFakeDbSet(fakeCab, cabeceras.AsQueryable());
            A.CallTo(() => db.CabPedidoVtas).Returns(fakeCab);
            DbSet<CondPagoCliente> fakeCond = A.Fake<DbSet<CondPagoCliente>>(o => o.Implements(typeof(IQueryable<CondPagoCliente>)).Implements(typeof(IDbAsyncEnumerable<CondPagoCliente>)));
            ConfigurarFakeDbSet(fakeCond, new List<CondPagoCliente>().AsQueryable());
            A.CallTo(() => db.CondPagoClientes).Returns(fakeCond);

            IGestorStocks stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._, A<int>._)).Returns(SugeridorModoServicio.ROSA);
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._)).Returns(SugeridorModoServicio.ROSA);
            return new PedidosVentaController(db)
            {
                LectorParametros = A.Fake<ILectorParametrosUsuario>(),
                Stocks = () => stocks
            };
        }

        private static PedidoVentaDTO Pedido(string cliente, int numero = 0) => new PedidoVentaDTO
        {
            empresa = "1",
            numero = numero,
            cliente = cliente, // un cliente por test: la sugerencia de servicio va con caché por huella
            contacto = "0",
            plazosPago = "CONTADO",
            periodoFacturacion = "NRM",
            Lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { id = 1, Producto = "17404", Cantidad = 200, PrecioUnitario = 1, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO }
            }
        };

        [TestMethod]
        public void PostOpcionesModos_PlantillaEnTodoAhora_CasillaVisibleYMarcada()
        {
            PedidosVentaController controller = Controller();
            PedidoVentaDTO pedido = Pedido("OPC688A");
            pedido.modoServicio = S.SEGUN_VAYA_ENTRANDO;
            pedido.modoFacturacion = F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;

            var r = controller.PostOpcionesModos(pedido) as OkNegotiatedContentResult<OpcionesModosPedidoDTO>;

            Assert.IsNotNull(r);
            Assert.IsTrue(r.Content.Facturacion.Visible);
            Assert.AreEqual(F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, r.Content.Facturacion.Modo);
            Assert.IsTrue(r.Content.ServirPendienteAlHaberStock.Visible);
            Assert.IsTrue(r.Content.ServirPendienteAlHaberStock.Valor);
            Assert.IsNotNull(r.Content.Servicio);
            Assert.IsTrue(r.Content.Servicio.ModosPermitidos.Any());
        }

        [TestMethod]
        public void PostOpcionesModos_PlantillaSinModoServicio_UsaElSugerido()
        {
            PedidosVentaController controller = Controller();
            PedidoVentaDTO pedido = Pedido("OPC688B");

            var r = controller.PostOpcionesModos(pedido) as OkNegotiatedContentResult<OpcionesModosPedidoDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual(r.Content.Servicio.Modo != S.TODO_JUNTO, r.Content.Facturacion.Visible,
                "Sin modo elegido manda el sugerido (lo que la plantilla preselecciona)");
            Assert.IsFalse(r.Content.ServirPendienteAlHaberStock.Visible, "Sin Todo ahora no hay casilla");
        }

        [TestMethod]
        public void PostOpcionesModos_PedidoGuardadoTodoJunto_OcultaLaFacturacion()
        {
            PedidosVentaController controller = Controller(new CabPedidoVta { Empresa = "1", Número = 688001, ModoServicio = S.TODO_JUNTO, ServirJunto = true, ModoFacturacion = F.AL_COMPLETAR, MantenerJunto = true });
            PedidoVentaDTO pedido = Pedido("OPC688C", 688001);
            pedido.mantenerJunto = true;

            var r = controller.PostOpcionesModos(pedido) as OkNegotiatedContentResult<OpcionesModosPedidoDTO>;

            Assert.IsNotNull(r);
            Assert.IsFalse(r.Content.Facturacion.Visible);
            Assert.AreEqual(OpcionesModosPedido.MOTIVO_FACTURACION_TODO_JUNTO, r.Content.Facturacion.MotivoOculta);
        }

        [TestMethod]
        public void Contrato3_PutQueSaleDeTodoAhoraConLaCasillaANull_NiRechazaNiToca()
        {
            OpcionesModosPedido.ResolucionCasilla r = OpcionesModosPedido.ResolverAlGuardar(null, F.POR_ENTREGAS, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES);

            Assert.IsNull(r.Error, "Nesto manda null con la casilla oculta: no se rechaza");
            Assert.IsNull(r.AGuardar);
            Assert.IsFalse(OpcionesModosPedido.Efectivo(true, F.POR_ENTREGAS, esNotaEntrega: false), "Fuera de «Todo ahora» lo guardado deja de contar solo");
            Assert.IsNull(OpcionesModosPedido.ParaLeer(true, F.POR_ENTREGAS, esNotaEntrega: false), "Al leer, null: no aplica");
        }

        [TestMethod]
        public void Contrato4_PostOpcionesModos_PedidoSinLineas_ContestaLaFacturacion()
        {
            PedidosVentaController controller = Controller();
            PedidoVentaDTO sinLineas = Pedido("OPC688D");
            sinLineas.Lineas = new List<LineaPedidoVentaDTO>();
            sinLineas.modoFacturacion = F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;
            PedidoVentaDTO lineasNull = Pedido("OPC688E");
            lineasNull.Lineas = null;

            var r = controller.PostOpcionesModos(sinLineas) as OkNegotiatedContentResult<OpcionesModosPedidoDTO>;
            var r2 = controller.PostOpcionesModos(lineasNull) as OkNegotiatedContentResult<OpcionesModosPedidoDTO>;

            Assert.IsNotNull(r);
            Assert.IsNotNull(r.Content.Facturacion);
            CollectionAssert.AreEqual(new List<byte> { 1, 2, 3 }, r.Content.Facturacion.ModosPermitidos);
            Assert.IsTrue(r.Content.ServirPendienteAlHaberStock.Valor);
            Assert.IsNotNull(r2);
            Assert.IsNotNull(r2.Content.Facturacion);
        }

        [TestMethod]
        public void Contrato5_DetalleReenviaLoLeido_ValorEsElGuardado_YAlPasarATodoAhoraMarcada()
        {
            // Pedido en «Todo ahora» con la casilla desmarcada: el GET da false, Nesto lo reenvía y vuelve false.
            Assert.AreEqual(false, OpcionesModosPedido.ParaLeer(false, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, esNotaEntrega: false));
            Assert.IsFalse(Calcular(S.SEGUN_VAYA_ENTRANDO, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, informado: false, almacenado: false).ServirPendienteAlHaberStock.Valor);
            // Marcada: true.
            Assert.AreEqual(true, OpcionesModosPedido.ParaLeer(true, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, esNotaEntrega: false));
            // Pedido «Por entregas»: el GET da null; el usuario pasa a «Todo ahora» y Nesto reenvía null → marcada.
            bool? leido = OpcionesModosPedido.ParaLeer(null, F.POR_ENTREGAS, esNotaEntrega: false);
            Assert.IsNull(leido);
            Assert.IsTrue(Calcular(S.SEGUN_VAYA_ENTRANDO, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, informado: leido).ServirPendienteAlHaberStock.Valor);
            // Y el PUT con ese null en «Todo ahora» la guarda marcada.
            Assert.AreEqual(true, OpcionesModosPedido.ResolverAlGuardar(leido, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, F.POR_ENTREGAS).AGuardar);
        }

        [TestMethod]
        public void Contrato6_PlantillaANullConTodoAhora_Marcada()
        {
            Assert.IsTrue(Calcular(S.TRAS_REPONER_DE_TIENDAS, F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, informado: null).ServirPendienteAlHaberStock.Valor);
        }

        [TestMethod]
        public void Contrato5_PostOpcionesModos_PedidoGuardadoEnTodoAhora_ValorEsElGuardado()
        {
            PedidosVentaController controller = Controller(new CabPedidoVta { Empresa = "1", Número = 688002, ModoServicio = S.SEGUN_VAYA_ENTRANDO, ModoFacturacion = F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES });
            var repositorio = A.Fake<IRepositorioServirPendienteAlHaberStock>();
            A.CallTo(() => repositorio.Leer("1", 688002)).Returns(false);
            controller.RepositorioServirPendiente = repositorio;
            PedidoVentaDTO pedido = Pedido("OPC688F", 688002);

            var r = controller.PostOpcionesModos(pedido) as OkNegotiatedContentResult<OpcionesModosPedidoDTO>;

            Assert.IsNotNull(r);
            Assert.IsTrue(r.Content.ServirPendienteAlHaberStock.Visible);
            Assert.IsFalse(r.Content.ServirPendienteAlHaberStock.Valor, "La guardada (desmarcada), no la de por defecto");
        }

        [TestMethod]
        public void GuardarServirPendiente_GuardaLoResuelto_YDejaElEfectivoEnElDto()
        {
            PedidosVentaController controller = Controller();
            var repositorio = A.Fake<IRepositorioServirPendienteAlHaberStock>();
            controller.RepositorioServirPendiente = repositorio;
            var todoAhora = new PedidoVentaDTO { empresa = "1", numero = 5, modoFacturacion = F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES };
            var porEntregas = new PedidoVentaDTO { empresa = "1", numero = 6, modoFacturacion = F.POR_ENTREGAS };

            controller.GuardarServirPendiente(todoAhora, true);
            controller.GuardarServirPendiente(porEntregas, null);

            A.CallTo(() => repositorio.Guardar("1", 5, true)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(true, todoAhora.servirPendienteAlHaberStock);
            Assert.IsNull(porEntregas.servirPendienteAlHaberStock);
            A.CallTo(() => repositorio.Leer(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GuardarServirPendiente_SiFallaElRepositorio_NoLanza()
        {
            PedidosVentaController controller = Controller();
            var repositorio = A.Fake<IRepositorioServirPendienteAlHaberStock>();
            A.CallTo(() => repositorio.Guardar(A<string>._, A<int>._, A<bool>._)).Throws(new System.Exception("sin columna"));
            controller.RepositorioServirPendiente = repositorio;

            controller.GuardarServirPendiente(new PedidoVentaDTO { empresa = "1", numero = 5, modoFacturacion = F.TODO_AHORA_Y_LO_PENDIENTE_DESPUES }, true);
        }

        [TestMethod]
        public void PostOpcionesModos_SinPedido_400()
        {
            Assert.IsInstanceOfType(Controller().PostOpcionesModos(null), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void ResolverServirPendiente_DelControlador_UsaElModoNormalizado()
        {
            PedidosVentaController controller = Controller();
            var pedido = new PedidoVentaDTO { modoFacturacion = F.POR_ENTREGAS, servirPendienteAlHaberStock = true };

            string error = controller.ResolverServirPendiente(pedido, null, out bool? aGuardar);

            Assert.AreEqual(OpcionesModosPedido.MOTIVO_RECHAZO_CASILLA, error);
            Assert.IsNull(aGuardar);
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}

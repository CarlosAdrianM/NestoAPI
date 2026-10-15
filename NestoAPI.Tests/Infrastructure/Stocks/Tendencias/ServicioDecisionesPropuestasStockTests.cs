using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#631: transiciones de decisión, qué escribe «Aplicar» y los silencios.</summary>
    [TestClass]
    public class ServicioDecisionesPropuestasStockTests
    {
        private static readonly DateTime Hoy = new DateTime(2026, 10, 15, 10, 0, 0);
        private IRepositorioDecisionesPropuestasStock repositorio;
        private RegistroDecisionPropuesta guardado;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioDecisionesPropuestasStock>();
            guardado = null;
            _ = A.CallTo(() => repositorio.GuardarDecision(A<RegistroDecisionPropuesta>._)).Invokes((RegistroDecisionPropuesta r) => guardado = r);
            _ = A.CallTo(() => repositorio.LeerEstadoProducto("17404")).Returns((short?)0);
            _ = A.CallTo(() => repositorio.LeerControlesStock("17404")).Returns(new List<ControlStockActual>
            {
                new ControlStockActual { Almacen = "ALG", StockMaximo = 555, StockMinimo = 295 },
                new ControlStockActual { Almacen = "REI", StockMaximo = 6, StockMinimo = 3 },
                new ControlStockActual { Almacen = "ALC", StockMaximo = 0, StockMinimo = 0 }
            });
        }

        private ServicioDecisionesPropuestasStock Servicio() => new ServicioDecisionesPropuestasStock(repositorio, () => Hoy);

        private PropuestaStockConDecision Propuesta(string accion = AccionesPropuesta.AJUSTAR, string decision = null, string estado = EstadosPropuesta.PENDIENTE)
        {
            PropuestaStockConDecision p = new PropuestaStockConDecision
            {
                Id = 7,
                Mes = new DateTime(2026, 9, 1),
                Producto = "17404",
                Almacen = "ALG",
                EstadoProducto = 0,
                Accion = accion,
                Multiplos = 144,
                StockMaximoActual = 555,
                StockMinimoActual = 295,
                StockMaximoPropuesto = accion == AccionesPropuesta.AJUSTAR ? 364 : (int?)null,
                StockMinimoPropuesto = accion == AccionesPropuesta.AJUSTAR ? 220 : (int?)null,
                Decision = decision,
                DecisionFecha = decision == null ? (DateTime?)null : new DateTime(2026, 10, 2, 9, 0, 0),
                Estado = estado
            };
            _ = A.CallTo(() => repositorio.LeerPropuesta(7)).Returns(p);
            return p;
        }

        private HttpStatusCode Falla(DecisionPropuestaStockDTO peticion)
        {
            try
            {
                _ = Servicio().Decidir(7, peticion, "NUEVAVISION\\Manuel");
            }
            catch (DecisionPropuestaStockException ex)
            {
                A.CallTo(() => repositorio.GuardarDecision(A<RegistroDecisionPropuesta>._)).MustNotHaveHappened();
                return ex.Codigo;
            }
            Assert.Fail("Tenía que fallar");
            return HttpStatusCode.OK;
        }

        // ---------- Aplicar «Ajustar máx/mín» ----------

        [TestMethod]
        public void Aplicar_Ajuste_EscribeLosPropuestosEnElAlmacenConValorAnteriorYQuienYCuando()
        {
            _ = Propuesta();

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "aplicar" }, "NUEVAVISION\\Manuel");

            Assert.AreEqual(EstadosPropuesta.APLICADA, r.Estado);
            Assert.AreEqual(DecisionesPropuesta.APLICAR, guardado.Decision);
            Assert.AreEqual("NUEVAVISION\\Manuel", guardado.Usuario);
            Assert.AreEqual(Hoy, guardado.Fecha);
            Assert.AreEqual(555, guardado.StockMaximoAnterior);
            Assert.AreEqual(295, guardado.StockMinimoAnterior);
            Assert.AreEqual(364, guardado.StockMaximoAplicado);
            Assert.AreEqual(220, guardado.StockMinimoAplicado);
            Assert.IsNull(guardado.EstadoAplicado);
            CambioControlStock cambio = guardado.CambiosControlStock.Single();
            Assert.AreEqual("ALG", cambio.Almacen);
            Assert.IsTrue(cambio.Existia);
            Assert.AreEqual(555, cambio.MaximoAnterior);
            Assert.AreEqual(364, cambio.MaximoNuevo);
            Assert.AreEqual(220, cambio.MinimoNuevo);
            Assert.AreEqual("ALG: máximo 555 → 364, mínimo 295 → 220", guardado.Cambios);
            Assert.IsNull(guardado.DecisionAnterior);
        }

        [TestMethod]
        public void Aplicar_AjusteConOtrosValoresYMotivo_EscribeLosDeLaPersona()
        {
            _ = Propuesta();

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar", StockMaximo = 400, StockMinimo = 250, Motivo = "Feria en noviembre" }, "Manuel");

            Assert.AreEqual(400, guardado.CambiosControlStock.Single().MaximoNuevo);
            Assert.AreEqual(250, guardado.StockMinimoAplicado);
            Assert.AreEqual("Feria en noviembre", guardado.Motivo);
        }

        [TestMethod]
        public void Aplicar_AjusteConOtrosValoresSinMotivo_400()
        {
            _ = Propuesta();

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", StockMaximo = 400 }));
        }

        [TestMethod]
        public void Aplicar_AjusteConMinimoMayorQueMaximoOFueraDeRango_400()
        {
            _ = Propuesta();

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", StockMaximo = 10, StockMinimo = 20, Motivo = "x" }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", StockMaximo = 40000, StockMinimo = 20, Motivo = "x" }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", StockMaximo = 10, StockMinimo = -1, Motivo = "x" }));
        }

        [TestMethod]
        public void Aplicar_AjusteSinControlDeStock_LoCrea()
        {
            _ = Propuesta();
            _ = A.CallTo(() => repositorio.LeerControlesStock("17404")).Returns(new List<ControlStockActual>());

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar" }, "Manuel");

            CambioControlStock cambio = guardado.CambiosControlStock.Single();
            Assert.IsFalse(cambio.Existia);
            Assert.AreEqual(144, cambio.Multiplos);
            Assert.IsNull(guardado.StockMaximoAnterior);
            Assert.AreEqual("ALG: control de stock nuevo con máximo 364 y mínimo 220", guardado.Cambios);
        }

        [TestMethod]
        public void Aplicar_AjusteQueYaEstaba_SeApuntaSinEscribir()
        {
            _ = Propuesta();
            _ = A.CallTo(() => repositorio.LeerControlesStock("17404")).Returns(new List<ControlStockActual>
            {
                new ControlStockActual { Almacen = "ALG", StockMaximo = 364, StockMinimo = 220 }
            });

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar" }, "Manuel");

            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
            Assert.AreEqual(EstadosPropuesta.APLICADA, r.Estado);
            StringAssert.Contains(r.Mensaje, "no se ha cambiado nada");
        }

        [TestMethod]
        public void Aplicar_ProductoQueHaCambiadoDeEstado_409()
        {
            _ = Propuesta();
            _ = A.CallTo(() => repositorio.LeerEstadoProducto("17404")).Returns((short?)3);

            Assert.AreEqual(HttpStatusCode.Conflict, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar" }));
        }

        // ---------- Aplicar «Pasar a estado 1» ----------

        [TestMethod]
        public void Aplicar_Estado1_PoneAceroTodosLosMaximosYCambiaElEstado()
        {
            _ = Propuesta(AccionesPropuesta.ESTADO_1);

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar" }, "Manuel");

            // ALC ya estaba a 0: no se toca. ALG y REI sí (el trigger no deja cambiar el estado con máximos).
            CollectionAssert.AreEqual(new[] { "ALG", "REI" }, guardado.CambiosControlStock.Select(c => c.Almacen).ToArray());
            Assert.IsTrue(guardado.CambiosControlStock.All(c => c.MaximoNuevo == 0 && c.MinimoNuevo == 0));
            Assert.AreEqual((short)0, guardado.EstadoAnterior);
            Assert.AreEqual((short)1, guardado.EstadoAplicado);
            Assert.AreEqual(555, guardado.StockMaximoAnterior);
            Assert.AreEqual(0, guardado.StockMaximoAplicado);
            CollectionAssert.Contains(r.Cambios, "Estado 0 → 1");
        }

        [TestMethod]
        public void Aplicar_Estado1ConMaximo_400()
        {
            _ = Propuesta(AccionesPropuesta.ESTADO_1);

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", StockMaximo = 3, Motivo = "x" }));
        }

        [TestMethod]
        public void Aplicar_Estado1YaEnEstado1_409()
        {
            PropuestaStockConDecision p = Propuesta(AccionesPropuesta.ESTADO_1);
            p.EstadoProducto = 1;
            _ = A.CallTo(() => repositorio.LeerEstadoProducto("17404")).Returns((short?)1);

            Assert.AreEqual(HttpStatusCode.Conflict, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar" }));
        }

        // ---------- Aplicar sin escritura ----------

        [TestMethod]
        public void Aplicar_LiquidarConCanal_SoloSeApunta()
        {
            _ = Propuesta(AccionesPropuesta.LIQUIDAR);

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "outlet" }, "Manuel");

            Assert.AreEqual(CanalesLiquidacion.OUTLET, guardado.CanalLiquidacion);
            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
            Assert.IsNull(guardado.EstadoAplicado);
            A.CallTo(() => repositorio.LeerEstadoProducto(A<string>._)).MustNotHaveHappened();
            StringAssert.Contains(r.Mensaje, "No se cambia nada automáticamente");
        }

        [TestMethod]
        public void Aplicar_LiquidarConCanalDesconocido_400()
        {
            _ = Propuesta(AccionesPropuesta.LIQUIDAR);

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "Amazon" }));
        }

        [TestMethod]
        public void Aplicar_CanalEnUnaPropuestaQueNoEsLiquidar_400()
        {
            _ = Propuesta(AccionesPropuesta.PARAR_COMPRAS);

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "Outlet" }));
        }

        [TestMethod]
        public void Aplicar_PararCompras_SoloSeApunta()
        {
            _ = Propuesta(AccionesPropuesta.PARAR_COMPRAS);

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar" }, "Manuel");

            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
            Assert.IsNull(guardado.Cambios);
        }

        // ---------- Rechazar, posponer ----------

        [TestMethod]
        public void Rechazar_SinMotivo_400_ConMotivo_Rechazada()
        {
            _ = Propuesta();
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Rechazar" }));

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Rechazar", Motivo = "Lo vamos a anunciar en revista" }, "Manuel");

            Assert.AreEqual(EstadosPropuesta.RECHAZADA, r.Estado);
            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
            A.CallTo(() => repositorio.LeerControlesStock(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Posponer_NoEscribeNadaYQuedaPospuesta()
        {
            _ = Propuesta();

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }, "Manuel");

            Assert.AreEqual(EstadosPropuesta.POSPUESTA, r.Estado);
            Assert.AreEqual(DecisionesPropuesta.POSPONER, guardado.Decision);
            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
            StringAssert.Contains(r.Mensaje, "mes que viene");
        }

        [TestMethod]
        public void Pospuesta_SePuedeVolverADecidir_YSeGuardaLoQueHabiaParaNoPisarAOtro()
        {
            PropuestaStockConDecision p = Propuesta(decision: DecisionesPropuesta.POSPONER, estado: EstadosPropuesta.POSPUESTA);

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar" }, "Manuel");

            Assert.AreEqual(DecisionesPropuesta.POSPONER, guardado.DecisionAnterior);
            Assert.AreEqual(p.DecisionFecha, guardado.DecisionFechaAnterior);
            Assert.AreEqual(DecisionesPropuesta.APLICAR, guardado.Decision);
        }

        // ---------- Silenciar ----------

        [TestMethod]
        public void Silenciar_ConMotivoYCaducidad_Silenciada()
        {
            _ = Propuesta();

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO
            {
                Decision = "Silenciar",
                MotivoSilencio = "estacional",
                SilenciarHasta = new DateTime(2027, 3, 31)
            }, "Manuel");

            Assert.AreEqual(EstadosPropuesta.SILENCIADA, r.Estado);
            Assert.AreEqual(MotivosSilencio.ESTACIONAL, guardado.MotivoSilencio);
            Assert.AreEqual(new DateTime(2027, 3, 31), guardado.SilenciadaHasta);
            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
        }

        [TestMethod]
        public void Silenciar_SinMotivoOConMotivoDesconocidoOSinFecha_400()
        {
            _ = Propuesta();

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Silenciar", SilenciarHasta = new DateTime(2027, 1, 1) }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Silenciar", MotivoSilencio = "Porque sí", SilenciarHasta = new DateTime(2027, 1, 1) }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Silenciar", MotivoSilencio = "Nuevo" }));
        }

        [TestMethod]
        public void Silenciar_CaducidadPasadaOMasDeDosAnios_400()
        {
            _ = Propuesta();

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Silenciar", MotivoSilencio = "Nuevo", SilenciarHasta = Hoy.Date }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Silenciar", MotivoSilencio = "Nuevo", SilenciarHasta = Hoy.AddMonths(25) }));
        }

        [TestMethod]
        public void MotivoDeSilencioEnOtraDecision_400()
        {
            _ = Propuesta();

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Posponer", MotivoSilencio = "Nuevo" }));
        }

        [TestMethod]
        public void Silenciada_SePuedeQuitarElSilencioDecidiendoOtraCosa()
        {
            _ = Propuesta(decision: DecisionesPropuesta.SILENCIAR, estado: EstadosPropuesta.SILENCIADA);

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }, "Manuel");

            Assert.IsNull(guardado.SilenciadaHasta);
            Assert.IsNull(guardado.MotivoSilencio);
            Assert.AreEqual(DecisionesPropuesta.SILENCIAR, guardado.DecisionAnterior);
        }

        // ---------- Transiciones que no se pueden ----------

        [TestMethod]
        public void Aplicada_NoSePuedeVolverADecidir_409()
        {
            _ = Propuesta(decision: DecisionesPropuesta.APLICAR, estado: EstadosPropuesta.APLICADA);

            Assert.AreEqual(HttpStatusCode.Conflict, Falla(new DecisionPropuestaStockDTO { Decision = "Rechazar", Motivo = "Me he equivocado" }));
        }

        [TestMethod]
        public void ConUnaPropuestaMasReciente_409()
        {
            PropuestaStockConDecision p = Propuesta(estado: EstadosPropuesta.SUPERADA);
            p.MesMasReciente = new DateTime(2026, 10, 1);

            Assert.AreEqual(HttpStatusCode.Conflict, Falla(new DecisionPropuestaStockDTO { Decision = "Posponer" }));
        }

        [TestMethod]
        public void RechazadaConUnaPropuestaMasReciente_409()
        {
            PropuestaStockConDecision p = Propuesta(decision: DecisionesPropuesta.RECHAZAR, estado: EstadosPropuesta.RECHAZADA);
            p.MesMasReciente = new DateTime(2026, 10, 1);

            Assert.AreEqual(HttpStatusCode.Conflict, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar" }));
        }

        [TestMethod]
        public void Mantener_NoHayNadaQueDecidir_400()
        {
            _ = Propuesta(AccionesPropuesta.MANTENER, estado: EstadosPropuesta.SIN_ACCION);

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar" }));
        }

        [TestMethod]
        public void DecisionDesconocidaOVacia_400()
        {
            _ = Propuesta();

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Borrar" }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(null));
        }

        [TestMethod]
        public void PropuestaQueNoExiste_404()
        {
            _ = A.CallTo(() => repositorio.LeerPropuesta(7)).Returns(null);

            Assert.AreEqual(HttpStatusCode.NotFound, Falla(new DecisionPropuestaStockDTO { Decision = "Posponer" }));
        }

        [TestMethod]
        public void MaximoEnUnaDecisionQueNoEsAplicar_400()
        {
            _ = Propuesta();

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Posponer", StockMaximo = 3 }));
        }

        [TestMethod]
        public void UsuarioLargo_SeCortaA50()
        {
            _ = Propuesta();

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }, new string('x', 80));

            Assert.AreEqual(50, guardado.Usuario.Length);
        }

        // ---------- Escritura de ControlesStock (compartida con PUT api/ControlesStock) ----------

        [TestMethod]
        public void ControlesStockEscritura_CambiaMaximoMinimoUsuarioYFecha()
        {
            ControlStock control = new ControlStock { StockMáximo = 555, StockMínimo = 295, Múltiplos = 144, Estacionalidad = "111111111111" };

            ControlesStockEscritura.CambiarMaximoMinimo(control, 220, 364, "NUEVAVISION\\UnUsuarioConUnNombreMuyMuyLargo", Hoy);

            Assert.AreEqual((short)364, control.StockMáximo);
            Assert.AreEqual((short)220, control.StockMínimo);
            Assert.AreEqual((short)144, control.Múltiplos);
            Assert.AreEqual(30, control.Usuario.Length); // ControlesStock.Usuario es varchar(30)
            Assert.AreEqual(Hoy, control.Fecha_Modificación);
        }

        [TestMethod]
        public void ControlesStockEscritura_NuevoConEstacionalidadPlanaYMultiploMinimo1()
        {
            ControlStock control = ControlesStockEscritura.Nuevo("1", "ALG", "17404", 220, 364, 0, "Manuel", Hoy);

            Assert.AreEqual("111111111111", control.Estacionalidad);
            Assert.AreEqual((short)1, control.Múltiplos);
            Assert.AreEqual("Manuel", control.Usuario);
        }
    }
}

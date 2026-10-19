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

        // ---------- Aplicar «Liquidar» (Carlos, 15/10/26: máximo y mínimo a 0 en el almacén de la propuesta) ----------

        [TestMethod]
        public void Aplicar_LiquidarPorElOutlet_PasaAEstado4_YPoneA0LosMaximosDeTodosLosAlmacenes()
        {
            // NestoAPI#628 (Carlos, 16/10/26): con el Outlet, estado 4 (el cuadre nocturno #643 solo mete productos en estado 4).
            _ = Propuesta(AccionesPropuesta.LIQUIDAR);

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "outlet" }, "Manuel");

            Assert.AreEqual(CanalesLiquidacion.OUTLET, guardado.CanalLiquidacion);
            // ALG (el de la propuesta) primero y REI (6/3) también: trgProductosUpd no deja salir del estado 0 con algún máximo.
            Assert.AreEqual(2, guardado.CambiosControlStock.Count);
            Assert.AreEqual("ALG", guardado.CambiosControlStock[0].Almacen);
            Assert.AreEqual("REI", guardado.CambiosControlStock[1].Almacen);
            Assert.AreEqual(0, guardado.CambiosControlStock[1].MaximoNuevo);
            Assert.AreEqual(555, guardado.StockMaximoAnterior);
            Assert.AreEqual(0, guardado.StockMaximoAplicado);
            // El estado, con el usuario de la decisión (lo escribe el repositorio por el mismo camino que «Pasar a estado 1»)
            Assert.AreEqual((short?)0, guardado.EstadoAnterior);
            Assert.AreEqual((short?)4, guardado.EstadoAplicado);
            Assert.AreEqual("Manuel", guardado.Usuario);
            Assert.AreEqual("ALG: máximo 555 → 0, mínimo 295 → 0; REI: máximo 6 → 0, mínimo 3 → 0; Estado 0 → 4", guardado.Cambios);
            Assert.AreEqual(EstadosPropuesta.APLICADA, r.Estado);
            StringAssert.Contains(r.Mensaje, "El producto pasa a estado 4: si tiene ficha en la tienda, entra en el Outlet en la pasada nocturna siguiente.");
        }

        [TestMethod]
        public void Aplicar_LiquidarSinCanal_CuentaComoOutlet()
        {
            _ = Propuesta(AccionesPropuesta.LIQUIDAR);

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar" }, "Manuel");

            Assert.IsNull(guardado.CanalLiquidacion);
            Assert.AreEqual((short?)4, guardado.EstadoAplicado);
        }

        [TestMethod]
        public void Aplicar_LiquidarPorElOutlet_YaEnEstado4AMano_SoloQuitaLosMaximos()
        {
            _ = Propuesta(AccionesPropuesta.LIQUIDAR);
            _ = A.CallTo(() => repositorio.LeerEstadoProducto("17404")).Returns((short?)4);

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "Outlet" }, "Manuel");

            Assert.IsNull(guardado.EstadoAplicado);
            Assert.AreEqual(2, guardado.CambiosControlStock.Count);
            StringAssert.Contains(r.Mensaje, "El producto ya estaba en estado 4");
        }

        [TestMethod]
        public void Aplicar_LiquidarPorElOutlet_SiElEstadoHaCambiadoAOtro_409()
        {
            _ = Propuesta(AccionesPropuesta.LIQUIDAR);
            _ = A.CallTo(() => repositorio.LeerEstadoProducto("17404")).Returns((short?)1);

            Assert.AreEqual(HttpStatusCode.Conflict, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "Outlet" }));
        }

        [TestMethod]
        public void Aplicar_LiquidarUnPropio_400()
        {
            PropuestaStockConDecision p = Propuesta(AccionesPropuesta.LIQUIDAR);
            p.Formula = FormulasPropuesta.PREFIJO_PROPIOS + " estado 0";

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "Outlet" }));
            try
            {
                _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar" }, "Manuel");
            }
            catch (DecisionPropuestaStockException ex)
            {
                StringAssert.StartsWith(ex.Message, "Los productos propios (fabricación propia) nunca se liquidan");
            }
        }

        [TestMethod]
        public void Aplicar_LiquidarPorGanavisiones_PoneAceroElMaximoYElMinimoDelAlmacenYApuntaElCanal_SinCambiarElEstado()
        {
            _ = Propuesta(AccionesPropuesta.LIQUIDAR);

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "ganavisiones" }, "Manuel");

            Assert.AreEqual(CanalesLiquidacion.GANAVISIONES, guardado.CanalLiquidacion);
            // Solo ALG (el de la propuesta): REI tiene 6/3 y no se toca.
            Assert.AreEqual(1, guardado.CambiosControlStock.Count);
            CambioControlStock cambio = guardado.CambiosControlStock[0];
            Assert.AreEqual("ALG", cambio.Almacen);
            Assert.IsTrue(cambio.Existia);
            Assert.AreEqual(555, cambio.MaximoAnterior);
            Assert.AreEqual(295, cambio.MinimoAnterior);
            Assert.AreEqual(0, cambio.MaximoNuevo);
            Assert.AreEqual(0, cambio.MinimoNuevo);
            // Valor anterior y aplicado en la fila, y el texto en el histórico.
            Assert.AreEqual(555, guardado.StockMaximoAnterior);
            Assert.AreEqual(295, guardado.StockMinimoAnterior);
            Assert.AreEqual(0, guardado.StockMaximoAplicado);
            Assert.AreEqual(0, guardado.StockMinimoAplicado);
            Assert.AreEqual("ALG: máximo 555 → 0, mínimo 295 → 0", guardado.Cambios);
            Assert.AreEqual("Manuel", guardado.Usuario);
            Assert.AreEqual(Hoy, guardado.Fecha);
            // No cambia el estado del producto.
            Assert.IsNull(guardado.EstadoAplicado);
            A.CallTo(() => repositorio.LeerEstadoProducto(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(EstadosPropuesta.APLICADA, r.Estado);
            CollectionAssert.AreEqual(new[] { "ALG: máximo 555 → 0, mínimo 295 → 0" }, r.Cambios);
            StringAssert.Contains(r.Mensaje, "El estado no cambia: el paso a Ganavisiones lo hace usted a mano.");
        }

        [TestMethod]
        public void Aplicar_LiquidarYaAcero_SeApuntaSinEscribir()
        {
            PropuestaStockConDecision p = Propuesta(AccionesPropuesta.LIQUIDAR);
            p.Almacen = "ALC";

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "Ganavisiones" }, "Manuel");

            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
            Assert.AreEqual(0, guardado.StockMaximoAnterior);
            Assert.AreEqual(0, guardado.StockMaximoAplicado);
            Assert.IsNull(guardado.Cambios);
            StringAssert.Contains(r.Mensaje, "ya estaban a 0");
        }

        [TestMethod]
        public void Aplicar_LiquidarSinControlDeStock_NoLoCrea()
        {
            PropuestaStockConDecision p = Propuesta(AccionesPropuesta.LIQUIDAR);
            p.Almacen = "TIE";

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar", CanalLiquidacion = "Ganavisiones" }, "Manuel");

            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
            Assert.IsNull(guardado.StockMaximoAnterior);
            Assert.IsNull(guardado.StockMaximoAplicado);
            StringAssert.Contains(r.Mensaje, "no hay control de stock en TIE");
        }

        // ---------- Aplicar sin escritura ----------

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

        // ---------- Motivos de silencio desde el parámetro MotivosSilencioPropuestas ----------

        [TestMethod]
        public void Silenciar_ConUnMotivoNuevoDelParametro_SeAceptaComoEstaEscritoEnElParametro()
        {
            _ = Propuesta();
            _ = A.CallTo(() => repositorio.LeerParametroMotivosSilencio()).Returns("Nuevo; Estacional, Aparato, Fin de serie");

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO
            {
                Decision = "Silenciar",
                MotivoSilencio = " fin de SERIE ",
                SilenciarHasta = new DateTime(2027, 3, 31)
            }, "Manuel");

            Assert.AreEqual(EstadosPropuesta.SILENCIADA, r.Estado);
            Assert.AreEqual("Fin de serie", guardado.MotivoSilencio);
        }

        [TestMethod]
        public void Silenciar_ConParametroQueQuitaUnMotivo_Ese400()
        {
            _ = Propuesta();
            _ = A.CallTo(() => repositorio.LeerParametroMotivosSilencio()).Returns("Nuevo, Estacional");

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Silenciar", MotivoSilencio = "Aparato", SilenciarHasta = new DateTime(2027, 1, 1) }));
        }

        [TestMethod]
        public void MotivosSilencioVigentes_SinParametroOVacio_LosTresDeLaIssue()
        {
            _ = A.CallTo(() => repositorio.LeerParametroMotivosSilencio()).Returns(null);
            CollectionAssert.AreEqual(new[] { "Nuevo", "Estacional", "Aparato" }, Servicio().MotivosSilencioVigentes());

            _ = A.CallTo(() => repositorio.LeerParametroMotivosSilencio()).Returns("  ;, ");
            CollectionAssert.AreEqual(new[] { "Nuevo", "Estacional", "Aparato" }, Servicio().MotivosSilencioVigentes());
        }

        [TestMethod]
        public void MotivosSilencio_Lista_SinRepetidosNiMasLargosQueLaColumna()
        {
            List<string> lista = MotivosSilencio.Lista("Nuevo, nuevo, Estacional, Un motivo de más de veinte letras, Aparato");

            CollectionAssert.AreEqual(new[] { "Nuevo", "Estacional", "Aparato" }, lista);
            Assert.AreEqual(20, MotivosSilencio.LONGITUD_MAXIMA);
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

        // ---------- NestoAPI#655: propios y «Potenciar ventas» ----------

        [TestMethod]
        public void Aplicar_AccionDePropios_EscribeElMaximoYElMinimoDeLaFormulaP()
        {
            foreach (string accion in new[] { AccionesPropuesta.AJUSTAR_LOTE, AccionesPropuesta.POTENCIAR, AccionesPropuesta.CADUCA })
            {
                PropuestaStockConDecision p = Propuesta(accion);
                p.StockMaximoPropuesto = 360;
                p.StockMinimoPropuesto = 90;

                ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar" }, "Manuel");

                Assert.AreEqual(EstadosPropuesta.APLICADA, r.Estado, accion);
                Assert.AreEqual("ALG: máximo 555 → 360, mínimo 295 → 90", guardado.Cambios, accion);
            }
        }

        [TestMethod]
        public void Aplicar_AccionDePropiosConOtrosValoresYMotivo_EscribeLosDeLaPersona()
        {
            PropuestaStockConDecision p = Propuesta(AccionesPropuesta.AJUSTAR_LOTE);
            p.StockMaximoPropuesto = 360;
            p.StockMinimoPropuesto = 90;

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Aplicar", StockMaximo = 300, StockMinimo = 90, Motivo = "Lote de 200 pactado" }, "Manuel");

            Assert.AreEqual(300, guardado.CambiosControlStock.Single().MaximoNuevo);
        }

        [TestMethod]
        public void Potenciar_ConObjetivo_SinFechaSonSeisMesesYNoTocaElStock()
        {
            _ = A.CallTo(() => repositorio.ExistePotenciacion()).Returns(true);
            PropuestaStockConDecision p = Propuesta(AccionesPropuesta.POTENCIAR);
            p.VentaMedia6Meses = 30m;

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "potenciar", ObjetivoVentaMensual = 40, Motivo = "Feria de Unión Láser" }, "Manuel");

            Assert.AreEqual(EstadosPropuesta.EN_POTENCIACION, r.Estado);
            Assert.AreEqual(DecisionesPropuesta.POTENCIAR, guardado.Decision);
            Assert.AreEqual(40, guardado.ObjetivoVentaMensual);
            Assert.AreEqual(new DateTime(2027, 4, 15), guardado.PotenciarHasta);
            Assert.AreEqual("Feria de Unión Láser", guardado.Motivo);
            Assert.AreEqual(0, guardado.CambiosControlStock.Count);
            Assert.IsNull(guardado.EstadoAplicado);
            Assert.IsNull(guardado.MotivoSilencio);
            Assert.AreEqual("Producto 17404 en potenciación hasta el 15/04/2027: objetivo 40 uds/mes (ahora vende 30,0 de media). " +
                "Si no se cumple, volverá a salir con la nota «potenciación sin cumplir».", r.Mensaje);
        }

        [TestMethod]
        public void Potenciar_ConFechaDeRevision_LaDeLaPersona()
        {
            _ = A.CallTo(() => repositorio.ExistePotenciacion()).Returns(true);
            _ = Propuesta(AccionesPropuesta.CADUCA);

            _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Potenciar", ObjetivoVentaMensual = 25, PotenciarHasta = new DateTime(2027, 1, 31, 18, 0, 0) }, "Manuel");

            Assert.AreEqual(new DateTime(2027, 1, 31), guardado.PotenciarHasta);
        }

        [TestMethod]
        public void Potenciar_SinObjetivoOConFechaPasadaOMasDeDosAnios_400()
        {
            _ = A.CallTo(() => repositorio.ExistePotenciacion()).Returns(true);
            _ = Propuesta(AccionesPropuesta.POTENCIAR);

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Potenciar" }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Potenciar", ObjetivoVentaMensual = 0 }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Potenciar", ObjetivoVentaMensual = 40, PotenciarHasta = Hoy.Date }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Potenciar", ObjetivoVentaMensual = 40, PotenciarHasta = Hoy.Date.AddMonths(25) }));
        }

        [TestMethod]
        public void ObjetivoOFechaDePotenciacionEnOtraDecision_400()
        {
            _ = A.CallTo(() => repositorio.ExistePotenciacion()).Returns(true);
            _ = Propuesta();

            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Posponer", ObjetivoVentaMensual = 40 }));
            Assert.AreEqual(HttpStatusCode.BadRequest, Falla(new DecisionPropuestaStockDTO { Decision = "Aplicar", PotenciarHasta = Hoy.AddMonths(1) }));
        }

        [TestMethod]
        public void Potenciar_SinElScript_404YNoGuarda_ElRestoDeDecisionesComoAntes()
        {
            _ = A.CallTo(() => repositorio.ExistePotenciacion()).Returns(false);
            _ = Propuesta(AccionesPropuesta.POTENCIAR);

            try
            {
                _ = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Potenciar", ObjetivoVentaMensual = 40 }, "Manuel");
                Assert.Fail("Tenía que fallar");
            }
            catch (DecisionPropuestaStockException ex)
            {
                Assert.AreEqual(HttpStatusCode.NotFound, ex.Codigo);
                StringAssert.Contains(ex.Message, "Issue655_PotenciarVentas.sql");
            }
            A.CallTo(() => repositorio.GuardarDecision(A<RegistroDecisionPropuesta>._)).MustNotHaveHappened();

            ResultadoDecisionPropuestaStock pospuesta = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }, "Manuel");
            Assert.AreEqual(EstadosPropuesta.POSPUESTA, pospuesta.Estado);
        }

        [TestMethod]
        public void SePuedeDecidirSobreUnaFilaEnPotenciacion()
        {
            _ = A.CallTo(() => repositorio.ExistePotenciacion()).Returns(true);
            _ = Propuesta(AccionesPropuesta.POTENCIAR, decision: DecisionesPropuesta.POTENCIAR, estado: EstadosPropuesta.EN_POTENCIACION);

            ResultadoDecisionPropuestaStock r = Servicio().Decidir(7, new DecisionPropuestaStockDTO { Decision = "Potenciar", ObjetivoVentaMensual = 50 }, "Manuel");

            Assert.AreEqual(EstadosPropuesta.EN_POTENCIACION, r.Estado);
            Assert.AreEqual(DecisionesPropuesta.POTENCIAR, guardado.DecisionAnterior);
            Assert.AreEqual(50, guardado.ObjetivoVentaMensual);
        }

        [TestMethod]
        public void NotaPotenciacion_VigenteCumplidaYSinCumplir()
        {
            PropuestaStockConDecision fila = new PropuestaStockConDecision { VentaMedia6Meses = 30m };
            Assert.IsNull(fila.NotaPotenciacion);

            fila.PotenciacionPropuestaId = 7;
            fila.PotenciacionObjetivo = 40;
            fila.PotenciacionHasta = new DateTime(2027, 4, 15);
            fila.PotenciacionVigente = true;
            Assert.AreEqual("En potenciación hasta el 15/04/2027: venta media 30,0 uds/mes de un objetivo de 40 (75 %)", fila.NotaPotenciacion);

            fila.PotenciacionVigente = false;
            Assert.AreEqual("potenciación sin cumplir (objetivo 40, real 30,0)", fila.NotaPotenciacion);

            fila.VentaMedia6Meses = 42.5m;
            Assert.AreEqual("potenciación cumplida (objetivo 40, real 42,5)", fila.NotaPotenciacion);
        }

        [TestMethod]
        public void ObjetivoVentaSugerido_EsLaVentaMediaPorUnoYMedioRedondeadaHaciaArriba()
        {
            Assert.AreEqual(45, new PropuestaStockConDecision { VentaMedia6Meses = 30m }.ObjetivoVentaSugerido);
            Assert.AreEqual(4, new PropuestaStockConDecision { VentaMedia6Meses = 2.1m }.ObjetivoVentaSugerido);
            Assert.AreEqual(1, new PropuestaStockConDecision { VentaMedia6Meses = 0.1m }.ObjetivoVentaSugerido);
        }

        [TestMethod]
        public void ObjetivoVentaSugerido_SinVentas_EsNull()
        {
            Assert.IsNull(new PropuestaStockConDecision { VentaMedia6Meses = 0m }.ObjetivoVentaSugerido);
        }

        [TestMethod]
        public void Sql_SinElScriptDePotenciacion_NoMiraLasColumnasNuevas()
        {
            string leer = RepositorioDecisionesPropuestasStockSql.SqlLeer(false);
            string guardar = RepositorioDecisionesPropuestasStockSql.SqlGuardar(false);
            string historico = RepositorioDecisionesPropuestasStockSql.SqlHistorico(false);

            Assert.IsFalse(leer.Contains("p.PotenciarHasta") || leer.Contains("pv."), leer);
            StringAssert.Contains(leer, "CAST(NULL AS date) AS PotenciarHasta");
            StringAssert.Contains(leer, "CAST(0 AS bit) AS PotenciacionVigente");
            Assert.IsFalse(leer.Contains("EnPotenciacion"));
            Assert.IsFalse(guardar.Contains("PotenciarHasta") || guardar.Contains("{0}"), guardar);
            Assert.IsFalse(historico.Contains("PotenciarHasta") || historico.Contains("{1}"), historico);
            // Las columnas de PropuestaStockConDecision con set salen en las dos variantes (si no, SqlQuery falla)
            foreach (string columna in new[] { "ObjetivoVentaMensual", "PotenciarHasta", "PotenciacionPropuestaId", "PotenciacionObjetivo", "PotenciacionHasta", "PotenciacionVigente" })
            {
                StringAssert.Contains(leer, columna);
                StringAssert.Contains(RepositorioDecisionesPropuestasStockSql.SqlLeer(true), columna);
            }
        }

        [TestMethod]
        public void Sql_ConElScriptDePotenciacion_EstadoEnPotenciacionYGuardaObjetivoYFecha()
        {
            string leer = RepositorioDecisionesPropuestasStockSql.SqlLeer(true);

            StringAssert.Contains(leer, "WHEN p.Decision = 'Potenciar' AND p.PotenciarHasta >= CAST(GETDATE() AS date) THEN 'EnPotenciacion'");
            StringAssert.Contains(leer, "WHEN pv.PotenciarHasta >= CAST(GETDATE() AS date) THEN 'EnPotenciacion'");
            StringAssert.Contains(leer, "x.Decision = 'Potenciar' AND x.Mes <= p.Mes");
            // Heredada: después de superada y silenciada; antes de pospuesta
            Assert.IsTrue(leer.IndexOf("WHEN s.Id IS NOT NULL", StringComparison.Ordinal) < leer.IndexOf("WHEN pv.PotenciarHasta >= CAST(GETDATE() AS date) THEN 'EnPotenciacion'", StringComparison.Ordinal));
            Assert.IsTrue(leer.IndexOf("WHEN pv.PotenciarHasta >= CAST(GETDATE() AS date) THEN 'EnPotenciacion'", StringComparison.Ordinal) < leer.IndexOf("WHEN p.Decision = 'Posponer'", StringComparison.Ordinal));
            StringAssert.Contains(RepositorioDecisionesPropuestasStockSql.SqlGuardar(true), "ObjetivoVentaMensual = @objetivo, PotenciarHasta = @potenciarHasta");
            StringAssert.Contains(RepositorioDecisionesPropuestasStockSql.SqlHistorico(true), "@cambios, @objetivo, @potenciarHasta)");
        }

        // ---------- Escritura de ControlesStock (compartida con PUT api/ControlesStock) ----------

        [TestMethod]
        public void ControlesStockEscritura_CambiaMaximoMinimoUsuarioYFecha()
        {
            ControlStock control = new ControlStock { Almacén = "ALG", StockMáximo = 555, StockMínimo = 295, Múltiplos = 144, Estacionalidad = "111111111111" };

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

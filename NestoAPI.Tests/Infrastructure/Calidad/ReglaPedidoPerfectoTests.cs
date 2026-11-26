using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad;
using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914: LA REGLA del pedido perfecto, motivo a motivo y sus bordes.</summary>
    [TestClass]
    public class ReglaPedidoPerfectoTests
    {
        // Jueves 05/11/2026; viernes 06/11; lunes 09/11. Festivo de prueba: martes 08/12/2026 (Inmaculada).
        private static readonly DateTime JUEVES = new DateTime(2026, 11, 5);
        private static readonly DateTime VIERNES = new DateTime(2026, 11, 6);
        private static readonly DateTime SABADO = new DateTime(2026, 11, 7);
        private static readonly DateTime LUNES = new DateTime(2026, 11, 9);
        private static readonly DateTime MARTES = new DateTime(2026, 11, 10);
        private static readonly DateTime FESTIVO = new DateTime(2026, 12, 8);

        private static bool EsFestivo(DateTime dia, string almacen) =>
            dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday || dia.Date == FESTIVO
            || (almacen == Constantes.Almacenes.REINA && dia.Date == new DateTime(2026, 11, 9)); // la Almudena, solo en Reina

        private static ContextoPedidoPerfecto Contexto(Action<ContextoPedidoPerfecto> cambiar = null)
        {
            var c = new ContextoPedidoPerfecto
            {
                Hoy = new DateTime(2026, 11, 26),
                EsFestivo = EsFestivo,
                FotosStockDesde = new DateTime(2026, 11, 11),
                QuitarPickingDesde = new DateTime(2026, 11, 9),
                EntregaIncompletaDesde = new DateTime(2026, 11, 19)
            };
            cambiar?.Invoke(c);
            return c;
        }

        /// <summary>El pedido perfecto: del jueves, servido el viernes en un albarán, contado, un envío entregado.</summary>
        private static HechosPedidoPerfecto Perfecto(Action<HechosPedidoPerfecto> cambiar = null)
        {
            var h = new HechosPedidoPerfecto
            {
                Empresa = "1",
                Pedido = 930001,
                Fecha = JUEVES,
                Cliente = "40483",
                Contacto = "0",
                PlazosPago = "CONTADO",
                FormaPago = "RCB",
                ModoServicio = 1,
                Almacen = "ALG",
                LineasProducto = 3,
                LineasServidas = 3,
                Albaranes = 1,
                PrimerAlbaran = VIERNES,
                MinFechaEntrega = JUEVES,
                EnviosSalidos = 1
            };
            cambiar?.Invoke(h);
            return h;
        }

        private static EvaluacionPedidoPerfecto Evaluar(HechosPedidoPerfecto h, ContextoPedidoPerfecto c = null) =>
            ReglaPedidoPerfecto.Evaluar(h, c ?? Contexto());

        [TestMethod]
        public void PedidoPerfecto_SinMotivos()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto());

            Assert.IsTrue(e.Perfecto, string.Join(", ", e.Motivos));
            Assert.AreEqual(1, e.DiasLaborablesHastaServir);
            Assert.AreEqual(ReglaPedidoPerfecto.Canales.VENDEDOR, e.Canal);
        }

        // 1. De una vez
        [TestMethod]
        public void DosAlbaranes_NoServidoDeUnaVez()
        {
            CollectionAssert.Contains(Evaluar(Perfecto(h => h.Albaranes = 2)).Motivos, MotivoPedidoPerfecto.NoServidoDeUnaVez);
        }

        [TestMethod]
        public void ServidoConAlgoPendiente_NoServidoDeUnaVez()
        {
            CollectionAssert.Contains(Evaluar(Perfecto(h => { h.LineasServidas = 2; h.LineasPendientes = 1; })).Motivos,
                MotivoPedidoPerfecto.NoServidoDeUnaVez);
        }

        [TestMethod]
        public void NadaServido_NoEsNoServidoDeUnaVezSinoSinServir()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.LineasServidas = 0; h.LineasPendientes = 3; h.Albaranes = 0; h.PrimerAlbaran = null; }));

            CollectionAssert.DoesNotContain(e.Motivos, MotivoPedidoPerfecto.NoServidoDeUnaVez);
            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.SinServir);
            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.NoSalioAlDiaSiguiente));
        }

        // 2. Sin servir: el plazo
        [TestMethod]
        public void SinServirDentroDelPlazo_NoEvaluado()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h =>
            {
                h.Fecha = new DateTime(2026, 11, 20);
                h.MinFechaEntrega = h.Fecha;
                h.LineasServidas = 0; h.LineasPendientes = 3; h.Albaranes = 0; h.PrimerAlbaran = null;
            }));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.SinServir));
            CollectionAssert.DoesNotContain(e.Motivos, MotivoPedidoPerfecto.SinServir);
        }

        [TestMethod]
        public void SinServirConFechaDeEntregaFutura_ElPlazoCuentaDesdeEsaFecha()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h =>
            {
                h.Fecha = new DateTime(2026, 10, 1);
                h.MinFechaEntrega = new DateTime(2026, 11, 20);
                h.LineasServidas = 0; h.LineasPendientes = 3; h.Albaranes = 0; h.PrimerAlbaran = null;
            }));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.SinServir));
        }

        [TestMethod]
        public void SinServirSinFecha2099_NoEvaluado()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h =>
            {
                h.MinFechaEntrega = new DateTime(2099, 12, 31);
                h.LineasServidas = 0; h.LineasPendientes = 3; h.Albaranes = 0; h.PrimerAlbaran = null;
            }));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.SinServir));
        }

        // 3. Al día siguiente laborable
        [TestMethod]
        public void ViernesServidoElLunes_Perfecto()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.Fecha = VIERNES; h.MinFechaEntrega = VIERNES; h.PrimerAlbaran = LUNES; }));

            Assert.AreEqual(1, e.DiasLaborablesHastaServir);
            CollectionAssert.DoesNotContain(e.Motivos, MotivoPedidoPerfecto.NoSalioAlDiaSiguiente);
        }

        [TestMethod]
        public void ViernesServidoElMartes_NoSalioAlDiaSiguiente()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.Fecha = VIERNES; h.MinFechaEntrega = VIERNES; h.PrimerAlbaran = MARTES; }));

            Assert.AreEqual(2, e.DiasLaborablesHastaServir);
            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.NoSalioAlDiaSiguiente);
        }

        [TestMethod]
        public void SabadoServidoElMartes_UnLaborable_Perfecto()
        {
            // El sábado no se trabaja: el lunes hace de «día del pedido» y el martes es el siguiente
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.Fecha = SABADO; h.MinFechaEntrega = SABADO; h.PrimerAlbaran = MARTES; }));

            Assert.AreEqual(1, e.DiasLaborablesHastaServir);
            Assert.IsTrue(e.Perfecto);
        }

        [TestMethod]
        public void ServidoElMismoDia_CeroDias()
        {
            Assert.AreEqual(0, Evaluar(Perfecto(h => h.PrimerAlbaran = JUEVES)).DiasLaborablesHastaServir);
        }

        [TestMethod]
        public void LunesServidoElMiercolesConFestivoEnMedio_Perfecto()
        {
            // Lunes 07/12, martes 08/12 festivo, miércoles 09/12: un laborable
            DateTime lunes = new DateTime(2026, 12, 7);
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.Fecha = lunes; h.MinFechaEntrega = lunes; h.PrimerAlbaran = new DateTime(2026, 12, 9); }));

            Assert.AreEqual(1, e.DiasLaborablesHastaServir);
            Assert.IsTrue(e.Perfecto);
        }

        [TestMethod]
        public void FestivoDeLaTiendaCuentaSoloEnLaTienda()
        {
            // Viernes 06/11 → martes 10/11, con el lunes 09/11 festivo solo en Reina
            HechosPedidoPerfecto algete = Perfecto(h => { h.Fecha = VIERNES; h.MinFechaEntrega = VIERNES; h.PrimerAlbaran = MARTES; });
            HechosPedidoPerfecto reina = Perfecto(h => { h.Fecha = VIERNES; h.MinFechaEntrega = VIERNES; h.PrimerAlbaran = MARTES; h.Almacen = "REI"; });

            Assert.AreEqual(2, Evaluar(algete).DiasLaborablesHastaServir);
            Assert.AreEqual(1, Evaluar(reina).DiasLaborablesHastaServir);
        }

        [TestMethod]
        public void FechaDeEntregaFuturaPedidaPorElCliente_SeCuentaDesdeEsaFecha()
        {
            // Pedido del jueves 05/11 que el cliente quiere el lunes 09/11: servido el martes 10/11 = 1 laborable
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.MinFechaEntrega = LUNES; h.PrimerAlbaran = MARTES; }));

            Assert.AreEqual(1, e.DiasLaborablesHastaServir);
            CollectionAssert.DoesNotContain(e.Motivos, MotivoPedidoPerfecto.NoSalioAlDiaSiguiente);
        }

        [TestMethod]
        public void ServidoAntesDeLaFechaPedida_CeroDias()
        {
            Assert.AreEqual(0, Evaluar(Perfecto(h => { h.MinFechaEntrega = MARTES; h.PrimerAlbaran = VIERNES; })).DiasLaborablesHastaServir);
        }

        [TestMethod]
        public void JuntoConLoPendiente_NoSeEvaluaElDiaSiguiente()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.ModoServicio = Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE; h.PrimerAlbaran = new DateTime(2026, 11, 20); }));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.NoSalioAlDiaSiguiente));
            CollectionAssert.DoesNotContain(e.Motivos, MotivoPedidoPerfecto.NoSalioAlDiaSiguiente);
            Assert.IsNotNull(e.DiasLaborablesHastaServir, "El dato se da igual, aunque no cuente");
        }

        // 4. Stock: solo con fotos
        [TestMethod]
        public void FaltabaStock_AntesDeLasFotos_NoEvaluado()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.FaltabaStockEnFoto = true));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.FaltabaStock));
            Assert.IsTrue(e.Perfecto);
        }

        [TestMethod]
        public void FaltabaStock_ConFotos_Cuenta()
        {
            DateTime dia = new DateTime(2026, 11, 12);
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.Fecha = dia; h.MinFechaEntrega = dia; h.PrimerAlbaran = dia.AddDays(1); h.FaltabaStockEnFoto = true; }));

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.FaltabaStock);
        }

        [TestMethod]
        public void FaltabaStock_SinTablaDeFotos_NoEvaluado()
        {
            DateTime dia = new DateTime(2026, 11, 12);
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.Fecha = dia; h.FaltabaStockEnFoto = true; }), Contexto(c => c.FotosStockDesde = null));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.FaltabaStock));
        }

        // 5. Contado (informativo)
        [DataTestMethod]
        [DataRow("CONTADO", false)]
        [DataRow("PRE", false)]
        [DataRow("CR", false)]
        [DataRow("1/30", true)]
        [DataRow("2/30", true)]
        [DataRow("E1/30", true)]
        public void Contado(string plazos, bool motivo)
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.PlazosPago = plazos));

            Assert.AreEqual(motivo, e.Motivos.Contains(MotivoPedidoPerfecto.NoCobradoAlContado));
            Assert.AreEqual(!motivo, e.Perfecto);
            Assert.IsTrue(e.PerfectoSinContarInformativos, "No cobrar al contado es informativo: no quita el perfecto «sin informativos»");
        }

        [TestMethod]
        public void NoCobradoAlContado_EsInformativo()
        {
            Assert.IsFalse(InfoMotivosPedidoPerfecto.EsError(MotivoPedidoPerfecto.NoCobradoAlContado));
            Assert.IsTrue(InfoMotivosPedidoPerfecto.Todos.Where(m => m != MotivoPedidoPerfecto.NoCobradoAlContado).All(InfoMotivosPedidoPerfecto.EsError));
        }

        [TestMethod]
        public void SinPlazosPago_NoEvaluado()
        {
            Assert.IsTrue(Evaluar(Perfecto(h => h.PlazosPago = "")).NoEvaluados.Contains(MotivoPedidoPerfecto.NoCobradoAlContado));
        }

        // 6-8
        [TestMethod]
        public void CopiaORectificativa()
        {
            CollectionAssert.Contains(Evaluar(Perfecto(h => h.TieneCopia = true)).Motivos, MotivoPedidoPerfecto.TieneRectificativaOAbono);
            CollectionAssert.Contains(Evaluar(Perfecto(h => h.TieneRectificativa = true)).Motivos, MotivoPedidoPerfecto.TieneRectificativaOAbono);
        }

        [TestMethod]
        public void Impagado_EsError()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.Impagado = true));

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.Impagado);
            Assert.IsFalse(e.PerfectoSinContarInformativos);
        }

        [TestMethod]
        public void Agencia_DosEnviosIncidenciaReenvioOVuelta()
        {
            CollectionAssert.Contains(Evaluar(Perfecto(h => h.EnviosSalidos = 2)).Motivos, MotivoPedidoPerfecto.IncidenciaDeAgenciaOReenvio);
            CollectionAssert.Contains(Evaluar(Perfecto(h => h.EnvioConIncidencia = true)).Motivos, MotivoPedidoPerfecto.IncidenciaDeAgenciaOReenvio);
            CollectionAssert.Contains(Evaluar(Perfecto(h => h.Reenvio = true)).Motivos, MotivoPedidoPerfecto.IncidenciaDeAgenciaOReenvio);
            CollectionAssert.Contains(Evaluar(Perfecto(h => h.HaVuelto = true)).Motivos, MotivoPedidoPerfecto.IncidenciaDeAgenciaOReenvio);
            Assert.IsTrue(Evaluar(Perfecto(h => { h.EnviosSalidos = 0; h.Reenvio = null; h.HaVuelto = null; })).Perfecto, "Sin envío (recoge el cliente, va junto con otro) no es incidencia");
        }

        [TestMethod]
        public void LecturaDeEnvios_IncidenciaYEntregaIncompleta()
        {
            var h = Perfecto(x => x.EnviosSalidos = 0);
            LectorPedidoPerfectoSql.AnadirEnvio(h, Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Constantes.Agencias.ESTADO_ENTREGADO, new DateTime(2026, 11, 20),
                "Entregado (reparto propio): 4 de 5 bultos. Falta RP251099-5", null, null, null);

            Assert.AreEqual(1, h.EnviosSalidos);
            Assert.AreEqual(1, h.EnviosRepartoPropio);
            Assert.IsTrue(h.EntregaIncompleta);
            Assert.IsFalse(h.EnvioConIncidencia);
            Assert.IsNull(h.Reenvio, "Sin la tabla no se sabe");

            LectorPedidoPerfectoSql.AnadirEnvio(h, 1, Constantes.Agencias.ESTADO_CERRADO_A_MANO, new DateTime(2026, 11, 21), "", false, false, null);
            Assert.IsTrue(h.EnvioConIncidencia, "Cerrado a mano = era un incidentado que la agencia nunca cerró");
            Assert.AreEqual(false, h.Reenvio);
        }

        [TestMethod]
        public void LecturaDeEnvios_EntregaCompletaDelRepartoNoEsIncompleta()
        {
            var h = Perfecto();
            LectorPedidoPerfectoSql.AnadirEnvio(h, Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Constantes.Agencias.ESTADO_ENTREGADO, new DateTime(2026, 11, 20),
                "Entregado (reparto propio)", null, null, false);

            Assert.IsFalse(h.EntregaIncompleta);
        }

        // 9. Tocado tras el picking
        [TestMethod]
        public void PickingQuitado_AntesDelRastro_NoEvaluado()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.PickingQuitado = true));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.TocadoTrasElPicking));
        }

        [TestMethod]
        public void PickingQuitado_DesdeElRastro_Tocado()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.Fecha = LUNES; h.MinFechaEntrega = LUNES; h.PrimerAlbaran = MARTES; h.PickingQuitado = true; }));

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.TocadoTrasElPicking);
        }

        [TestMethod]
        public void AmpliacionEnElMismoPedido_Tocado()
        {
            DateTime dia = new DateTime(2026, 11, 12);
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h =>
            {
                h.Fecha = dia; h.MinFechaEntrega = dia; h.PrimerAlbaran = dia.AddDays(1);
                h.EventosCalidad.Add(TiposEventoCalidad.AMPLIACION_CON_PICKING_MISMO_PEDIDO);
            }));

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.TocadoTrasElPicking);
            CollectionAssert.DoesNotContain(e.Motivos, MotivoPedidoPerfecto.PidioCambiarAlgoConPicking);
        }

        [TestMethod]
        public void TocadoTrasElPicking_ElRastroEmpiezaEnElPrimerEventoSiEsAnterior()
        {
            ContextoPedidoPerfecto c = Contexto(x =>
            {
                x.QuitarPickingDesde = null;
                x.PrimerEventoPorTipo[TiposEventoCalidad.QUITAR_PICKING] = new DateTime(2026, 11, 2, 9, 30, 0);
            });

            Assert.AreEqual(new DateTime(2026, 11, 2), c.EvaluadoDesde(MotivoPedidoPerfecto.TocadoTrasElPicking));
            Assert.IsFalse(Evaluar(Perfecto(), c).NoEvaluados.Contains(MotivoPedidoPerfecto.TocadoTrasElPicking));
        }

        // 10. Ruta propia forzada: sin eventos, nunca
        [TestMethod]
        public void RutaPropiaForzada_SinEventos_NoEvaluado()
        {
            Assert.IsTrue(Evaluar(Perfecto()).NoEvaluados.Contains(MotivoPedidoPerfecto.EnvioDeRutaPropiaForzado));
        }

        [TestMethod]
        public void RutaPropiaForzada_ConEventos_Cuenta()
        {
            ContextoPedidoPerfecto c = Contexto(x => x.PrimerEventoPorTipo[TiposEventoCalidad.ENVIO_RUTA_PROPIA_FUERA_DE_DIA] = new DateTime(2026, 11, 1));

            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.EventosCalidad.Add(TiposEventoCalidad.ENVIO_RUTA_PROPIA_FUERA_DE_DIA)), c);

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.EnvioDeRutaPropiaForzado);
        }

        // 11. Entrega incompleta del reparto
        [TestMethod]
        public void EntregaIncompleta_SinEnvioDelRepartoNoAplica()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto());

            Assert.IsFalse(e.NoEvaluados.Contains(MotivoPedidoPerfecto.EntregaIncompletaDelReparto));
            CollectionAssert.DoesNotContain(e.Motivos, MotivoPedidoPerfecto.EntregaIncompletaDelReparto);
        }

        [TestMethod]
        public void EntregaIncompleta_EnvioDelRepartoAntesDel19_NoEvaluado()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.EnviosRepartoPropio = 1; h.PrimerEnvioRepartoPropio = VIERNES; }));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.EntregaIncompletaDelReparto));
        }

        [TestMethod]
        public void EntregaIncompleta_DesdeEl19_Cuenta()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h =>
            {
                h.Fecha = new DateTime(2026, 11, 19); h.MinFechaEntrega = h.Fecha; h.PrimerAlbaran = new DateTime(2026, 11, 20);
                h.EnviosRepartoPropio = 1; h.PrimerEnvioRepartoPropio = new DateTime(2026, 11, 20); h.EntregaIncompleta = true;
            }));

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.EntregaIncompletaDelReparto);
        }

        // 12. Pidió cambiar algo con picking
        [TestMethod]
        public void CambioDeModoConPicking_SinTablaDeEventos_NoEvaluado()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.EventosCalidad.Add(TiposEventoCalidad.CAMBIO_DE_MODO_CON_PICKING)));

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.PidioCambiarAlgoConPicking));
            Assert.IsTrue(e.Perfecto);
        }

        [TestMethod]
        public void CambioDeModoConPicking_ConEventos_Cuenta()
        {
            ContextoPedidoPerfecto c = Contexto(x => x.PrimerEventoPorTipo[TiposEventoCalidad.CAMBIO_DE_MODO_CON_PICKING] = new DateTime(2026, 11, 1));

            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.EventosCalidad.Add(TiposEventoCalidad.CAMBIO_DE_MODO_CON_PICKING)), c);

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.PidioCambiarAlgoConPicking);
        }

        [TestMethod]
        public void CambioDeModoConPicking_PedidoAnteriorAlPrimerEvento_NoEvaluado()
        {
            ContextoPedidoPerfecto c = Contexto(x => x.PrimerEventoPorTipo[TiposEventoCalidad.CAMBIO_DE_MODO_CON_PICKING] = new DateTime(2026, 11, 26));

            Assert.IsTrue(Evaluar(Perfecto(), c).NoEvaluados.Contains(MotivoPedidoPerfecto.PidioCambiarAlgoConPicking));
        }

        [TestMethod]
        public void VariosMotivos_Ordenados()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => { h.Impagado = true; h.Albaranes = 2; h.PlazosPago = "1/30"; }));

            CollectionAssert.AreEqual(new List<MotivoPedidoPerfecto>
            {
                MotivoPedidoPerfecto.NoServidoDeUnaVez, MotivoPedidoPerfecto.NoCobradoAlContado, MotivoPedidoPerfecto.Impagado
            }, e.Motivos);
        }

        // Canal
        [DataTestMethod]
        [DataRow("15191", 0, 0, 0, 0, 0, ReglaPedidoPerfecto.Canales.INTERNO)]
        [DataRow("32624", 0, 0, 0, 0, 0, ReglaPedidoPerfecto.Canales.AMAZON)]
        [DataRow("40483", 1, 0, 0, 0, 0, ReglaPedidoPerfecto.Canales.AMAZON)]
        [DataRow("31517", 0, 0, 0, 0, 0, ReglaPedidoPerfecto.Canales.TIENDA_ONLINE)]
        [DataRow("40483", 0, 1, 0, 0, 0, ReglaPedidoPerfecto.Canales.TIENDA_ONLINE)]
        [DataRow("40483", 0, 0, 1, 0, 0, ReglaPedidoPerfecto.Canales.OTRO_CANAL_EXTERNO)]
        [DataRow("40483", 0, 0, 0, 1, 0, ReglaPedidoPerfecto.Canales.APP)]
        [DataRow("40483", 0, 0, 0, 0, 2, ReglaPedidoPerfecto.Canales.TIENDA_FISICA)]
        [DataRow("9500", 0, 0, 0, 0, 0, ReglaPedidoPerfecto.Canales.TIENDA_FISICA)]
        [DataRow("10458", 0, 0, 0, 0, 0, ReglaPedidoPerfecto.Canales.OTROS)]
        [DataRow("40483", 0, 0, 0, 0, 0, ReglaPedidoPerfecto.Canales.VENDEDOR)]
        public void Canal(string cliente, int amazon, int web, int otro, int app, int tienda, string canal)
        {
            HechosPedidoPerfecto h = Perfecto(x =>
            {
                x.Cliente = cliente; x.LineasAmazon = amazon; x.LineasTiendaOnline = web; x.LineasOtroCanalExterno = otro; x.LineasApp = app; x.LineasEnTienda = tienda;
            });

            Assert.AreEqual(canal, ReglaPedidoPerfecto.Canal(h));
        }

        [TestMethod]
        public void Canal_SoloCursos()
        {
            Assert.AreEqual(ReglaPedidoPerfecto.Canales.CURSOS, ReglaPedidoPerfecto.Canal(Perfecto(h => h.LineasCursos = h.LineasProducto)));
            Assert.AreEqual(ReglaPedidoPerfecto.Canales.VENDEDOR, ReglaPedidoPerfecto.Canal(Perfecto(h => h.LineasCursos = 1)));
        }

        [TestMethod]
        public void TodosLosMotivosTienenTextoDeUsted()
        {
            foreach (MotivoPedidoPerfecto m in InfoMotivosPedidoPerfecto.Todos)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(InfoMotivosPedidoPerfecto.Texto(m)), m.ToString());
                Assert.IsFalse(string.IsNullOrWhiteSpace(InfoMotivosPedidoPerfecto.PorQueNoEvaluado(m)), m.ToString());
            }
            Assert.AreEqual(12, InfoMotivosPedidoPerfecto.Todos.Count);
        }

        [TestMethod]
        public void PedidoDelTextoDeQuitarPicking()
        {
            Assert.AreEqual(930110, LectorPedidoPerfectoSql.PedidoDelTexto("Pedido 930110: sin picking (quitado por Pedro con «Quitar picking» de Nesto)"));
            Assert.AreEqual(930111, LectorPedidoPerfectoSql.PedidoDelTexto("Pedido 930111, líneas 3, 4: sin picking (quitado por Pedro …)"));
            Assert.IsNull(LectorPedidoPerfectoSql.PedidoDelTexto("Otra cosa"));
            Assert.IsNull(LectorPedidoPerfectoSql.PedidoDelTexto(null));
        }

        [TestMethod]
        public void Tandas_PorMeses()
        {
            List<Tuple<DateTime, DateTime>> tandas = LectorPedidoPerfectoSql.Tandas(new DateTime(2026, 9, 15), new DateTime(2026, 11, 5));

            Assert.AreEqual(3, tandas.Count);
            Assert.AreEqual(new DateTime(2026, 9, 15), tandas[0].Item1);
            Assert.AreEqual(new DateTime(2026, 10, 1), tandas[0].Item2);
            Assert.AreEqual(new DateTime(2026, 11, 1), tandas[2].Item1);
            Assert.AreEqual(new DateTime(2026, 11, 5), tandas[2].Item2);
        }

        [TestMethod]
        public void Lote_SinTablasOpcionales_NoLasNombra()
        {
            string sin = LectorPedidoPerfectoSql.Lote(false, false, false, false, false);
            string con = LectorPedidoPerfectoSql.Lote(true, true, true, true, true);

            foreach (string tabla in new[] { "EnviosAgenciaReenvios", "EnviosAgenciaHaVuelto", "RepartoBultosPendientes", "FechasEntregaFotos", "PedidosModoServicioOrigen" })
            {
                Assert.IsFalse(sin.Contains(tabla), tabla);
                Assert.IsTrue(con.Contains(tabla), tabla);
            }
            foreach (string prohibido in new[] { "TRY_CONVERT", "STRING_SPLIT", "STRING_AGG", "OFFSET", "FETCH" })
            {
                Assert.IsFalse(con.ToUpperInvariant().Contains(prohibido), prohibido);
            }
            StringAssert.Contains(con, "READ UNCOMMITTED");
        }
    }
}

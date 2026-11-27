using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad;
using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using NestoAPI.Infraestructure.NotasEntrega;
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
        public void NoCobradoAlContadoYTieneCopia_SonLosUnicosInformativos()
        {
            var informativos = new[] { MotivoPedidoPerfecto.NoCobradoAlContado, MotivoPedidoPerfecto.TieneCopia };
            Assert.IsTrue(informativos.All(m => !InfoMotivosPedidoPerfecto.EsError(m)));
            Assert.IsTrue(InfoMotivosPedidoPerfecto.Todos.Where(m => !informativos.Contains(m)).All(InfoMotivosPedidoPerfecto.EsError));
        }

        [TestMethod]
        public void SinPlazosPago_NoEvaluado()
        {
            Assert.IsTrue(Evaluar(Perfecto(h => h.PlazosPago = "")).NoEvaluados.Contains(MotivoPedidoPerfecto.NoCobradoAlContado));
        }

        // 6. Antes TieneRectificativaOAbono, partido en cuatro (Carlos 27/11/26)
        private static FacturaRectificadaPedidoPerfecto Rectificada(decimal facturadas, decimal delPedido, decimal sinLinea = 0, int pedidosDeLaFactura = 1) =>
            new FacturaRectificadaPedidoPerfecto
            {
                Factura = "NV26/001234",
                UnidadesFacturadas = facturadas,
                UnidadesRectificadasDelPedido = delPedido,
                UnidadesRectificadasSinLinea = sinLinea,
                PedidosDeLaFactura = pedidosDeLaFactura
            };

        [TestMethod]
        public void NotaDeEntregaDeLoPendiente_EntregaIncompleta_YNoCopia()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => LectorPedidoPerfectoSql.AnadirHijo(h, notaEntrega: true, comentarioDeNota: true)));

            CollectionAssert.AreEqual(new List<MotivoPedidoPerfecto> { MotivoPedidoPerfecto.EntregaIncompleta }, e.Motivos);
            Assert.IsFalse(e.PerfectoSinContarInformativos, "La entrega incompleta es error");
        }

        [DataTestMethod]
        [DataRow(true, false, true)]   // NotaEntrega = 1 del hijo: la marca firme (#542 siempre la pone)
        [DataRow(false, true, true)]   // Solo el comentario de la nota de ESTE pedido: respaldo
        [DataRow(true, true, true)]
        [DataRow(false, false, false)] // Ni marca ni comentario: una copia
        public void Hijo_NotaDeEntregaOCopia(bool notaEntrega, bool comentario, bool esNota)
        {
            Assert.AreEqual(esNota, ReglaPedidoPerfecto.EsNotaEntregaDeLoPendiente(notaEntrega, comentario));
            HechosPedidoPerfecto h = Perfecto(x => LectorPedidoPerfectoSql.AnadirHijo(x, notaEntrega, comentario));
            Assert.AreEqual(esNota, h.TieneNotaEntregaPendiente);
            Assert.AreEqual(!esNota, h.TieneCopia);
        }

        [TestMethod]
        public void CopiaQueNoEsNota_EsInformativa()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => LectorPedidoPerfectoSql.AnadirHijo(h, false, false)));

            CollectionAssert.AreEqual(new List<MotivoPedidoPerfecto> { MotivoPedidoPerfecto.TieneCopia }, e.Motivos);
            Assert.IsFalse(e.Perfecto, "Como NoCobradoAlContado: un motivo informativo también quita el «perfecto» a secas");
            Assert.IsTrue(e.PerfectoSinContarInformativos);
        }

        [TestMethod]
        public void NotaYCopiaALaVez_LosDosMotivos()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h =>
            {
                LectorPedidoPerfectoSql.AnadirHijo(h, true, true);
                LectorPedidoPerfectoSql.AnadirHijo(h, false, false);
            }));

            CollectionAssert.AreEqual(new List<MotivoPedidoPerfecto> { MotivoPedidoPerfecto.EntregaIncompleta, MotivoPedidoPerfecto.TieneCopia }, e.Motivos);
        }

        [DataTestMethod]
        [DataRow(3, 3, 0, 1, true)]    // todas las unidades
        [DataRow(3, 5, 0, 1, true)]    // más de las facturadas (rectificada dos veces): entera
        [DataRow(3, 2, 0, 1, false)]   // en parte
        [DataRow(3, 0, 3, 1, true)]    // enlace sin línea en una factura solo suya: cuentan
        [DataRow(3, 0, 3, 2, false)]   // sin línea en una factura de dos pedidos: no se le atribuyen
        [DataRow(3, 1, 2, 1, true)]
        [DataRow(0, 0, 0, 1, false)]   // nada de producto (solo portes): en parte
        public void FacturaRectificada_EnteraOEnParte(int facturadas, int delPedido, int sinLinea, int pedidos, bool entera)
        {
            FacturaRectificadaPedidoPerfecto f = Rectificada(facturadas, delPedido, sinLinea, pedidos);
            Assert.AreEqual(entera, ReglaPedidoPerfecto.EsRectificadaEntera(f));

            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.FacturasRectificadas.Add(f)));
            CollectionAssert.AreEqual(new List<MotivoPedidoPerfecto>
            {
                entera ? MotivoPedidoPerfecto.FacturaRectificadaEntera : MotivoPedidoPerfecto.FacturaRectificadaEnParte
            }, e.Motivos);
        }

        [TestMethod]
        public void UnaFacturaEnteraYOtraEnParte_SoloEntera()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h =>
            {
                h.FacturasRectificadas.Add(Rectificada(2, 1));
                h.FacturasRectificadas.Add(Rectificada(4, 4));
            }));

            CollectionAssert.AreEqual(new List<MotivoPedidoPerfecto> { MotivoPedidoPerfecto.FacturaRectificadaEntera }, e.Motivos);
        }

        [TestMethod]
        public void LecturaDeFacturaRectificada_SeAnadeConSusUnidades()
        {
            HechosPedidoPerfecto h = Perfecto();
            LectorPedidoPerfectoSql.AnadirFacturaRectificada(h, "NV26/001234   ", 5m, 2m, 1m, 1);

            Assert.AreEqual(1, h.FacturasRectificadas.Count);
            Assert.AreEqual("NV26/001234", h.FacturasRectificadas[0].Factura);
            Assert.AreEqual(3m, ReglaPedidoPerfecto.UnidadesRectificadas(h.FacturasRectificadas[0]));
            Assert.IsFalse(ReglaPedidoPerfecto.EsRectificadaEntera(h.FacturasRectificadas[0]));
        }

        /// <summary>
        /// El reparto no cambia el porcentaje de perfectos: lo que con el motivo antiguo (copia o rectificativa) no era perfecto sigue sin
        /// serlo, y lo perfecto lo sigue siendo. Solo la copia que no es nota pasa a informativa (sale del error, no del «perfecto» a secas).
        /// </summary>
        [TestMethod]
        public void PartirElMotivoAntiguo_NoCambiaElPorcentajeDePerfectos()
        {
            var pedidos = new List<HechosPedidoPerfecto>
            {
                Perfecto(),
                Perfecto(h => h.Pedido = 2),
                Perfecto(h => { h.Pedido = 3; LectorPedidoPerfectoSql.AnadirHijo(h, true, true); }),
                Perfecto(h => { h.Pedido = 4; h.FacturasRectificadas.Add(Rectificada(3, 3)); }),
                Perfecto(h => { h.Pedido = 5; h.FacturasRectificadas.Add(Rectificada(3, 1)); }),
                Perfecto(h => { h.Pedido = 6; LectorPedidoPerfectoSql.AnadirHijo(h, true, false); h.FacturasRectificadas.Add(Rectificada(3, 1)); }),
                Perfecto(h => { h.Pedido = 7; LectorPedidoPerfectoSql.AnadirHijo(h, false, false); })
            };
            // El motivo antiguo: alguna copia (de cualquier tipo) o alguna factura rectificada
            Func<HechosPedidoPerfecto, bool> antiguo = h => h.TieneNotaEntregaPendiente || h.TieneCopia || h.FacturasRectificadas.Count > 0;
            int perfectosAntes = pedidos.Count(h => !antiguo(h));
            int erroresAhora = pedidos.Count(h => h.TieneNotaEntregaPendiente || h.FacturasRectificadas.Count > 0);

            var calculo = new CalculoPedidoPerfecto
            {
                Empresa = "1",
                Contexto = Contexto(),
                Pedidos = pedidos.Select(h => new KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>(h, Evaluar(h))).ToList()
            };
            ResumenPedidoPerfectoDTO resumen = ServicioPedidoPerfecto.Resumir(calculo);

            Assert.AreEqual(2, perfectosAntes);
            Assert.AreEqual(perfectosAntes, resumen.Perfectos);
            Assert.AreEqual(pedidos.Count - erroresAhora, resumen.PerfectosSinContarInformativos, "La copia (pedido 7) ya no es error");
            int Con(MotivoPedidoPerfecto m) => resumen.Motivos.Single(x => x.Motivo == m.ToString()).Pedidos;
            Assert.AreEqual(1, Con(MotivoPedidoPerfecto.FacturaRectificadaEntera));
            Assert.AreEqual(2, Con(MotivoPedidoPerfecto.FacturaRectificadaEnParte));
            Assert.AreEqual(2, Con(MotivoPedidoPerfecto.EntregaIncompleta));
            Assert.AreEqual(1, Con(MotivoPedidoPerfecto.TieneCopia));
            Assert.IsFalse(resumen.Motivos.Any(x => x.Motivo == "TieneRectificativaOAbono"), "El nombre antiguo ya no sale");
            Assert.IsTrue(new[] { MotivoPedidoPerfecto.EntregaIncompleta, MotivoPedidoPerfecto.FacturaRectificadaEntera, MotivoPedidoPerfecto.FacturaRectificadaEnParte }
                .All(m => resumen.Motivos.Single(x => x.Motivo == m.ToString()).EsError));
        }

        [TestMethod]
        public void SqlHijos_ReconoceLaNotaPorLaMarcaYPorElComentarioDeEsePedido()
        {
            string sql = LectorPedidoPerfectoSql.SQL_RECTIFICATIVAS_IMPAGADOS;

            StringAssert.Contains(sql, "CAST(o.NotaEntrega AS bit)");
            StringAssert.Contains(sql, "LIKE N'%NOTA DE ENTREGA: pendiente de entregar del pedido ' + CAST(o.PedidoOrigen AS nvarchar(12)) + N' %'");
            StringAssert.Contains(CreadorNotaEntregaPendiente.ComentarioNota(null, 929555, 733462),
                CreadorNotaEntregaPendiente.PREFIJO_COMENTARIO + "929555 (albarán 733462)");
            StringAssert.Contains(sql, "o.[Nº Orden] = r.Linea");
            StringAssert.Contains(LectorPedidoPerfectoSql.SQL_FIN_LOTE, "DROP TABLE #r");
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
            Assert.AreEqual(16, InfoMotivosPedidoPerfecto.Todos.Count);
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

        // Universo con líneas negativas (cheque regalo, descuento de portes, devolución suelta: Carlos 26/11/26)
        [TestMethod]
        public void Universo_SoloQuitaLosPedidosSinProductoEnPositivo_NoLosQueLlevanAlgunaLineaNegativa()
        {
            string universo = LectorPedidoPerfectoSql.SQL_UNIVERSO;

            StringAssert.Contains(universo, "l.TipoLinea = 1 AND l.Cantidad > 0)");
            Assert.IsFalse(universo.Contains("Cantidad < 0"), "Una línea negativa ya no deja fuera el pedido");
            StringAssert.Contains(universo, "l.Estado = -3");
        }

        [TestMethod]
        public void SqlPedidos_ServidoPendienteYAlbaranes_SoloMiranLineasDeProductoEnPositivo()
        {
            string sql = LectorPedidoPerfectoSql.SQL_PEDIDOS;

            foreach (string columna in new[] { "AS LineasProducto", "AS Pendientes", "AS Servidas", "AS Albaranes", "AS PrimerAlbaran", "AS MinFechaEntrega", "AS Almacen" })
            {
                string linea = sql.Split('\n').Single(l => l.Contains(columna));
                StringAssert.Contains(linea, "l.TipoLinea = 1 AND l.Cantidad > 0", columna);
            }
            string importe = sql.Split('\n').Single(l => l.Contains("AS Importe"));
            Assert.IsFalse(importe.Contains("Cantidad"), "El importe suma todas las líneas");
            StringAssert.Contains(sql, "a.LineasNegativas");
        }

        [TestMethod]
        public void PedidoConChequeRegaloServidoDeUnaVez_EsPerfectoYElDetalleLoDice()
        {
            HechosPedidoPerfecto h = Perfecto(x => { x.LlevaLineasNegativas = true; x.Importe = 70m; });

            EvaluacionPedidoPerfecto e = Evaluar(h);
            PedidoPerfectoDTO detalle = ServicioPedidoPerfecto.Detalle(h, e);

            Assert.IsTrue(e.Perfecto, string.Join(", ", e.Motivos));
            Assert.IsTrue(detalle.LlevaLineasNegativas);
            Assert.AreEqual(70m, detalle.Importe);
            Assert.IsFalse(ServicioPedidoPerfecto.Detalle(Perfecto(), Evaluar(Perfecto())).LlevaLineasNegativas);
        }

        // 17. Error de envío reclamado (Carlos 27/11/26)

        private static NestoAPI.Infraestructure.Calidad.Reclamaciones.ResultadoReclamacion Reclamacion(bool evaluada,
            params NestoAPI.Infraestructure.Calidad.Reclamaciones.SubMotivoReclamacion[] subMotivos)
        {
            var r = new NestoAPI.Infraestructure.Calidad.Reclamaciones.ResultadoReclamacion { Pedido = 900, Linea = 1, Evaluada = evaluada };
            foreach (var s in subMotivos)
            {
                _ = r.SubMotivos.Add(s);
            }
            return r;
        }

        [TestMethod]
        public void ErrorDeEnvioReclamado_SinLineasMiradas_NoEvaluadoYSigueSiendoPerfecto()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto());

            Assert.IsTrue(e.NoEvaluados.Contains(MotivoPedidoPerfecto.ErrorDeEnvioReclamado));
            Assert.IsTrue(e.Perfecto);
        }

        [TestMethod]
        public void ErrorDeEnvioReclamado_ReenviadoSinCoste_EsError()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.ReclamacionesEnvio.Add(Reclamacion(true,
                NestoAPI.Infraestructure.Calidad.Reclamaciones.SubMotivoReclamacion.ReenviadoSinCoste))));

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.ErrorDeEnvioReclamado);
            Assert.IsFalse(e.PerfectoSinContarInformativos);
        }

        [TestMethod]
        public void ErrorDeEnvioReclamado_SoloAbonadoSinReposicion_NoEsMotivo()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h => h.ReclamacionesEnvio.Add(Reclamacion(true,
                NestoAPI.Infraestructure.Calidad.Reclamaciones.SubMotivoReclamacion.AbonadoSinReposicion))));

            CollectionAssert.DoesNotContain(e.Motivos, MotivoPedidoPerfecto.ErrorDeEnvioReclamado);
            Assert.IsFalse(e.NoEvaluados.Contains(MotivoPedidoPerfecto.ErrorDeEnvioReclamado));
        }

        [TestMethod]
        public void ErrorDeEnvioReclamado_CambiadoPorOtro_VaJuntoConLaRectificativa()
        {
            EvaluacionPedidoPerfecto e = Evaluar(Perfecto(h =>
            {
                h.ReclamacionesEnvio.Add(Reclamacion(true, NestoAPI.Infraestructure.Calidad.Reclamaciones.SubMotivoReclamacion.CambiadoPorOtro));
                h.FacturasRectificadas.Add(new FacturaRectificadaPedidoPerfecto { Factura = "NV26/1", UnidadesFacturadas = 3, UnidadesRectificadasDelPedido = 1, PedidosDeLaFactura = 1 });
            }));

            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.ErrorDeEnvioReclamado);
            CollectionAssert.Contains(e.Motivos, MotivoPedidoPerfecto.FacturaRectificadaEnParte);
        }
    }
}

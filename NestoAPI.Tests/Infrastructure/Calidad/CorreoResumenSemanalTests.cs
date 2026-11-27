using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Calidad.Almacen;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using NestoAPI.Infraestructure.Calidad.ResumenSemanal;
using NestoAPI.Infraestructure.Calidad.Tablero;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914: el correo semanal de calidad (regla, pintor y servicio con el correo simulado).</summary>
    [TestClass]
    public class CorreoResumenSemanalTests
    {
        private const string DESTINATARIO = "alguien@ejemplo.test";
        private static readonly DateTime LUNES = new DateTime(2026, 11, 23);
        private static readonly DateTime HOY = new DateTime(2026, 11, 30); // el lunes siguiente
        private static readonly DateTime AHORA = HOY.AddHours(7).AddMinutes(30);

        // ---------------------------------------------------------------- datos de prueba

        private static SemanaKpiGuardada Fila(string kpi, TipoDuenoKpi tipo, string dueno, DateTime semana, decimal? valor) => new SemanaKpiGuardada
        {
            Kpi = kpi,
            TipoDueno = tipo,
            Dueno = dueno,
            Semana = semana,
            Valor = valor,
            CalculadoEl = HOY
        };

        /// <summary>Pérdida desconocida de ALC: 312 esta semana y 180 de media las 4 anteriores (150, 170, 190, 210).</summary>
        private static List<SemanaKpiGuardada> SemanasPerdidaAlc()
        {
            var filas = new List<SemanaKpiGuardada> { Fila(KpiPerdidaDesconocida.CLAVE, TipoDuenoKpi.Almacen, "ALC", LUNES, 312m) };
            decimal[] previas = { 210m, 190m, 170m, 150m };
            for (int i = 0; i < previas.Length; i++)
            {
                filas.Add(Fila(KpiPerdidaDesconocida.CLAVE, TipoDuenoKpi.Almacen, "ALC", LUNES.AddDays(-7 * (i + 1)), previas[i]));
            }
            return filas;
        }

        private static UmbralesKpi UmbralPerdida() => new UmbralesKpi
        {
            Kpi = KpiPerdidaDesconocida.CLAVE,
            TipoDueno = TipoDuenoKpi.Almacen,
            CortePoorFair = 400m,
            CorteFairGood = 300m,
            CorteGoodFantastic = 100m,
            VigenteDesde = ReglaNotaKpi.DESDE_SIEMPRE
        };

        private static DatosResumenSemanal Datos(List<SemanaKpiGuardada> semanas = null, List<UmbralesKpi> umbrales = null) => new DatosResumenSemanal
        {
            Semana = LUNES,
            Hoy = HOY,
            GeneradoEl = AHORA,
            Tablero = new DatosTableroResumen
            {
                Semanas = semanas ?? SemanasPerdidaAlc(),
                Umbrales = umbrales ?? new List<UmbralesKpi> { UmbralPerdida() }
            },
            Almacen = new ResumenCalidadAlmacenDTO
            {
                DiasReclamacionEnvio = 45,
                Preparadores = new List<PreparadorAlmacenDTO>
                {
                    new PreparadorAlmacenDTO
                    {
                        Preparador = "Andrey",
                        Lineas = 40,
                        ErrorDeEnvioReclamado = new IndicadorPreparadorDTO { Evaluadas = 0, Con = 0 },
                        FaltaEnPacking = new IndicadorPreparadorDTO { Evaluadas = 40, Con = 1, Porcentaje = 2.5m }
                    }
                },
                Total = new PreparadorAlmacenDTO
                {
                    Lineas = 40,
                    ErrorDeEnvioReclamado = new IndicadorPreparadorDTO { Evaluadas = 0, Con = 0 },
                    FaltaEnPacking = new IndicadorPreparadorDTO { Evaluadas = 40, Con = 1, Porcentaje = 2.5m }
                }
            },
            EncuestasSombraEncendida = true,
            Encuestas = new SimulacionEncuestasDTO
            {
                Dias = 7,
                DiasProcesados = 7,
                DiasEntre = 60,
                Candidatas = 30,
                Encuestas = 12,
                PerdidasPorTope = 5,
                PorTema = new List<TemaEncuestasDTO>
                {
                    new TemaEncuestasDTO { Tema = "PreparacionEntrega", Candidatas = 20, Encuestas = 9 },
                    new TemaEncuestasDTO { Tema = "Vendedor", Candidatas = 10, Encuestas = 3 },
                    new TemaEncuestasDTO { Tema = "AdministracionCobro", Candidatas = 0, Encuestas = 0 }
                },
                PorCanal = new List<CanalEncuestasDTO>
                {
                    new CanalEncuestasDTO { Canal = CanalesEncuesta.PUSH, Encuestas = 4 },
                    new CanalEncuestasDTO { Canal = CanalesEncuesta.CORREO, Encuestas = 8 }
                }
            }
        };

        // ---------------------------------------------------------------- LA REGLA

        [TestMethod]
        public void Asunto_SemanaDelMismoMes_CruzaMes_CruzaAnio()
        {
            Assert.AreEqual("Calidad: semana del 23 al 29/11/2026", ReglaCorreoResumenSemanal.Asunto(LUNES));
            Assert.AreEqual("Calidad: semana del 30/11 al 06/12/2026", ReglaCorreoResumenSemanal.Asunto(new DateTime(2026, 12, 2)));
            Assert.AreEqual("Calidad: semana del 28/12/2026 al 03/01/2027", ReglaCorreoResumenSemanal.Asunto(new DateTime(2026, 12, 28)));
        }

        [TestMethod]
        public void SemanaCerrada_ElLunesEsLaDeLunesADomingoAnterior()
        {
            Assert.AreEqual(LUNES, ReglaCorreoResumenSemanal.SemanaCerrada(HOY));
            Assert.AreEqual(LUNES, ReglaCorreoResumenSemanal.SemanaCerrada(new DateTime(2026, 12, 6)));
        }

        [TestMethod]
        public void Cambios_EnLenguajeLlano_ConNotaYMediaDeLas4Anteriores()
        {
            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos());

            Assert.AreEqual("Calidad: semana del 23 al 29/11/2026", correo.Asunto);
            Assert.AreEqual(1, correo.Cambios.Count);
            Assert.AreEqual("Pérdida desconocida en Alcobendas: 312 € (FAIR), sube respecto a las 4 semanas anteriores (180 €), a peor.", correo.Cambios[0]);
        }

        [TestMethod]
        public void Cambios_ComoMucho5_PrimeroLosQueMasCambianEnProporcion()
        {
            var semanas = new List<SemanaKpiGuardada>();
            string[] almacenes = { "ALG", "REI", "ALC" };
            // Pérdida: ALG ×2, REI ×4, ALC ×1,5 (sobre 100). Inventario: los tres ×3 (sobre 10).
            decimal[] perdida = { 200m, 400m, 150m };
            for (int a = 0; a < almacenes.Length; a++)
            {
                semanas.Add(Fila(KpiPerdidaDesconocida.CLAVE, TipoDuenoKpi.Almacen, almacenes[a], LUNES, perdida[a]));
                semanas.Add(Fila(KpiDiferenciasInventario.CLAVE, TipoDuenoKpi.Almacen, almacenes[a], LUNES, 30m));
                for (int i = 1; i <= 4; i++)
                {
                    semanas.Add(Fila(KpiPerdidaDesconocida.CLAVE, TipoDuenoKpi.Almacen, almacenes[a], LUNES.AddDays(-7 * i), 100m));
                    semanas.Add(Fila(KpiDiferenciasInventario.CLAVE, TipoDuenoKpi.Almacen, almacenes[a], LUNES.AddDays(-7 * i), 10m));
                }
            }

            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos(semanas, new List<UmbralesKpi>()));

            Assert.AreEqual(ReglaCorreoResumenSemanal.MAXIMO_CAMBIOS, correo.Cambios.Count);
            StringAssert.StartsWith(correo.Cambios[0], "Pérdida desconocida en Reina: 400 €, sube");
            Assert.IsTrue(correo.Cambios.Skip(1).Take(3).All(c => c.StartsWith("Diferencias de inventario")), string.Join(" | ", correo.Cambios));
            StringAssert.StartsWith(correo.Cambios[4], "Pérdida desconocida en Algete");
        }

        [TestMethod]
        public void Cambios_SinSemanasAnteriores_LoDice()
        {
            var semanas = new List<SemanaKpiGuardada> { Fila(KpiPerdidaDesconocida.CLAVE, TipoDuenoKpi.Almacen, "ALG", LUNES, 50m) };

            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos(semanas));

            Assert.AreEqual(1, correo.Cambios.Count);
            StringAssert.StartsWith(correo.Cambios[0], "Nada ha cambiado");
        }

        [TestMethod]
        public void Tablero_FilaConNotaYTiraDe8_DeLaMasAntiguaALaMasNueva()
        {
            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos());

            KpiCorreo perdida = correo.Tablero.Kpis.Single(k => k.Clave == KpiPerdidaDesconocida.CLAVE);
            Assert.AreEqual(LUNES, perdida.SemanaMostrada);
            Assert.IsNull(perdida.Linea);
            FilaKpiCorreo fila = perdida.Filas.Single();
            Assert.AreEqual("Alcobendas", fila.Dueno);
            Assert.AreEqual("312 €", fila.Valor);
            Assert.AreEqual("FAIR", fila.Nota);
            // 210, 190, 170, 150 → GOOD (≤ 300 y > 100); las 3 más antiguas, sin dato.
            CollectionAssert.AreEqual(new[] { "–", "–", "–", "G", "G", "G", "G", "F" }, fila.Tira);
        }

        [TestMethod]
        public void Tablero_SinUmbrales_SinUmbralYPendienteConLaRutaDeLaPropuesta()
        {
            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos(umbrales: new List<UmbralesKpi>()));

            FilaKpiCorreo fila = correo.Tablero.Kpis.Single(k => k.Clave == KpiPerdidaDesconocida.CLAVE).Filas.Single();
            Assert.AreEqual("sin umbral", fila.Nota);
            Assert.AreEqual(ReglaCorreoResumenSemanal.TIRA_SIN_NOTA, fila.Tira.Last());
            Assert.IsTrue(correo.Pendientes.Any(p => p.Contains("Pérdida desconocida") && p.Contains("GET api/Calidad/Tablero/Umbrales/Propuesta?kpi=PerdidaDesconocida")));
            Assert.IsTrue(correo.Pendientes.Any(p => p.Contains("api/Calidad/Tablero/Umbrales/Propuesta?kpi=PedidoPerfecto")));
            Assert.IsFalse(correo.Pendientes.Any(p => p.Contains("kpi=ReenviadoSinCoste")), "El que no tiene fuente no pide umbrales");
            StringAssert.StartsWith(correo.Cambios.Single(), "Pérdida desconocida en Alcobendas: 312 €, sube", "Sin umbral no se inventa nota");
        }

        [TestMethod]
        public void Tablero_KpiQueMadura_DiceCuandoEstaraYEnsenaLaUltimaCerrada()
        {
            DateTime ultimaCerrada = SemanaKpi.UltimaCerrada(21, HOY); // 02/11/2026 (cerrada el 30/11)
            var semanas = SemanasPerdidaAlc();
            semanas.Add(Fila(KpiPedidoPerfecto.CLAVE, TipoDuenoKpi.Empresa, "1", ultimaCerrada, 61.5m));

            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos(semanas));

            KpiCorreo pp = correo.Tablero.Kpis.Single(k => k.Clave == KpiPedidoPerfecto.CLAVE);
            StringAssert.StartsWith(pp.Linea, "La semana del 23/11/2026 estará el 21/12/2026");
            StringAssert.Contains(pp.Linea, "la del " + ultimaCerrada.ToString("dd/MM/yyyy"));
            Assert.AreEqual(ultimaCerrada, pp.SemanaMostrada);
            Assert.AreEqual("Toda la empresa", pp.Filas.Single().Dueno);
            Assert.AreEqual("61,5 %", pp.Filas.Single().Valor);
        }

        [TestMethod]
        public void Tablero_SinFuente_TodaviaNoDisponible_YSinCalcular_LoDice()
        {
            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos());

            StringAssert.StartsWith(correo.Tablero.Kpis.Single(k => k.Clave == KpiReenviadoSinCoste.CLAVE).Linea, "Todavía no disponible");
            KpiCorreo inventario = correo.Tablero.Kpis.Single(k => k.Clave == KpiDiferenciasInventario.CLAVE);
            Assert.AreEqual(0, inventario.Filas.Count);
            StringAssert.Contains(inventario.Linea, "no está calculada");
        }

        [TestMethod]
        public void TableroNoDisponible_ElRestoSaleIgual()
        {
            DatosResumenSemanal datos = Datos();
            datos.Tablero = null;
            datos.TableroSinTablas = true;
            datos.TableroNoDisponible = TableroKpiJobsService.MENSAJE_SIN_TABLAS;

            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(datos);

            StringAssert.StartsWith(correo.Tablero.NoDisponible, "No disponible: Todavía no existen dbo.CalidadKpiSemanas");
            StringAssert.StartsWith(correo.Cambios.Single(), "No disponible");
            Assert.AreEqual(2, correo.Almacen.Filas.Count);
            Assert.AreEqual(2, correo.Encuestas.PorTema.Count);
            Assert.IsTrue(correo.Pendientes.Any(p => p.Contains(TableroKpiJobsService.SCRIPT)));
        }

        [TestMethod]
        public void Almacen_LosDosOficialesConElRecuento()
        {
            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos());

            FilaAlmacenCorreo andrey = correo.Almacen.Filas[0];
            Assert.AreEqual("Andrey", andrey.Preparador);
            Assert.AreEqual("sin evaluar", andrey.ErrorDeEnvioReclamado);
            Assert.AreEqual("2,5 % (1 de 40)", andrey.FaltaEnPacking);
            Assert.IsTrue(correo.Almacen.Filas[1].EsTotal);
            Assert.AreEqual("Total", correo.Almacen.Filas[1].Preparador);
            Assert.IsTrue(correo.Almacen.Notas.Any(n => n.Contains("45 días")));
        }

        [TestMethod]
        public void Almacen_NoDisponible_LoDice()
        {
            DatosResumenSemanal datos = Datos();
            datos.Almacen = null;
            datos.AlmacenNoDisponible = "no se han podido calcular los indicadores de Almacén (tiempo agotado).";

            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(datos);

            Assert.AreEqual("No disponible: no se han podido calcular los indicadores de Almacén (tiempo agotado).", correo.Almacen.NoDisponible);
            Assert.AreEqual(0, correo.Almacen.Filas.Count);
        }

        [TestMethod]
        public void Encuestas_PorTemaYCanalYElTope()
        {
            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos());

            StringAssert.StartsWith(correo.Encuestas.Lineas[0], "Se habrían mandado 12 encuestas (de 30 candidatas) en los 7 de 7 días procesados");
            StringAssert.Contains(correo.Encuestas.Lineas[1], "ha parado 5 candidatas");
            Assert.AreEqual("Preparación y entrega", correo.Encuestas.PorTema[0].Que);
            Assert.AreEqual(9, correo.Encuestas.PorTema[0].Encuestas);
            Assert.AreEqual(2, correo.Encuestas.PorTema.Count, "Los temas a cero no salen");
            Assert.AreEqual("Push (app)", correo.Encuestas.PorCanal[0].Que);
            Assert.AreEqual(8, correo.Encuestas.PorCanal[1].Encuestas);
        }

        [TestMethod]
        public void Encuestas_ApagadoYSinTablas_LoDiceYQuedaPendiente()
        {
            DatosResumenSemanal datos = Datos();
            datos.EncuestasSombraEncendida = false;
            datos.Encuestas = null;
            datos.EncuestasSinTablas = true;
            datos.EncuestasNoDisponible = ServicioEncuestas.MENSAJE_SIN_TABLAS;

            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(datos);

            StringAssert.StartsWith(correo.Encuestas.Lineas.Single(), "El job de las encuestas en sombra está apagado");
            StringAssert.StartsWith(correo.Encuestas.NoDisponible, "No disponible: Faltan las tablas de las encuestas");
            Assert.IsTrue(correo.Pendientes.Any(p => p.Contains("Calidad.EncuestasSombra = 1")));
            Assert.IsTrue(correo.Pendientes.Any(p => p.Contains("Scripts/Issue914e_CalidadEncuestas.sql")));
        }

        [TestMethod]
        public void Pendientes_SinNada_NadaPendiente_YPieConLasRutas()
        {
            var umbrales = CatalogoKpiTablero.Todos.Where(k => k.TieneFuente)
                .SelectMany(k => k.TiposDueno.Select(t => new UmbralesKpi { Kpi = k.Clave, TipoDueno = t, VigenteDesde = ReglaNotaKpi.DESDE_SIEMPRE }))
                .ToList();

            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(Datos(umbrales: umbrales));

            Assert.AreEqual("Nada pendiente.", correo.Pendientes.Single());
            Assert.IsTrue(correo.Pie.Any(p => p.Contains("GET api/Calidad/Tablero?semana=2026-11-23")));
            Assert.IsTrue(correo.Pie.Any(p => p.Contains("GET api/Calidad/Almacen/Resumen?desde=2026-11-23&hasta=2026-11-30")));
            Assert.IsTrue(correo.Pie.Any(p => p.Contains("GET api/Calidad/Encuestas/Sombra?dias=7")));
            Assert.IsTrue(correo.Pie.Any(p => p.Contains("solo para usted")));
        }

        [TestMethod]
        public void Almacen_SinRastroQuePideScript_VaAPendientes()
        {
            DatosResumenSemanal datos = Datos();
            datos.Almacen.SinRastro.Add(new IndicadorSinRastroAlmacenDTO
            {
                Indicador = "FaltaQueSiEstaba",
                HaceFalta = "Lanzar Scripts/Issue914c_PreparacionEscaneosHueco.sql."
            });

            CorreoResumenSemanal correo = ReglaCorreoResumenSemanal.Componer(datos);

            Assert.IsTrue(correo.Pendientes.Any(p => p == "Almacén, FaltaQueSiEstaba: Lanzar Scripts/Issue914c_PreparacionEscaneosHueco.sql."));
        }

        [TestMethod]
        public void TableroSinLaSemana_SoloSiNingunKpiSinMadurarTieneFilas()
        {
            Assert.IsFalse(ReglaCorreoResumenSemanal.TableroSinLaSemana(Datos()));
            DatosResumenSemanal sinSemana = Datos(SemanasPerdidaAlc().Where(s => s.Semana != LUNES).ToList());
            Assert.IsTrue(ReglaCorreoResumenSemanal.TableroSinLaSemana(sinSemana));
            sinSemana.Tablero = null;
            Assert.IsFalse(ReglaCorreoResumenSemanal.TableroSinLaSemana(sinSemana), "Sin tablero no se aplaza: no va a aparecer");
        }

        [TestMethod]
        public void DesdeLectura_CubreLaTiraYLaTendenciaDelKpiQueMasMadura()
        {
            DateTime desde = ReglaCorreoResumenSemanal.DesdeLectura(CatalogoKpiTablero.Todos, LUNES, HOY);

            Assert.AreEqual(SemanaKpi.UltimaCerrada(21, HOY).AddDays(-7 * 11), desde);
        }

        // ---------------------------------------------------------------- El pintor

        [TestMethod]
        public void Pintar_HtmlConLasSecciones_LaTiraConLetrasYTodoEscapado()
        {
            DatosResumenSemanal datos = Datos();
            datos.Almacen.Preparadores[0].Preparador = "<b>Andrey</b>";

            string html = PintorCorreoResumenSemanal.Pintar(ReglaCorreoResumenSemanal.Componer(datos));

            StringAssert.Contains(html, "<meta name=\"viewport\"");
            StringAssert.Contains(html, "max-width: 640px");
            StringAssert.Contains(html, "Calidad: semana del 23 al 29/11/2026");
            StringAssert.Contains(html, "Lo que m&#225;s ha cambiado");
            StringAssert.Contains(html, "Pendiente de usted");
            StringAssert.Contains(html, "Encuestas en sombra");
            StringAssert.Contains(html, ">F</span>");
            StringAssert.Contains(html, ">G</span>");
            StringAssert.Contains(html, "&lt;b&gt;Andrey&lt;/b&gt;");
            Assert.IsFalse(html.Contains("<b>Andrey"));
            Assert.IsFalse(html.Contains("<script"));
        }

        [TestMethod]
        public void Pintar_SeccionNoDisponible_SaleElMotivo()
        {
            DatosResumenSemanal datos = Datos();
            datos.Encuestas = null;
            datos.EncuestasNoDisponible = "no se han podido leer";

            string html = PintorCorreoResumenSemanal.Pintar(ReglaCorreoResumenSemanal.Componer(datos));

            StringAssert.Contains(html, "No disponible: no se han podido leer");
        }

        // ---------------------------------------------------------------- El servicio (correo simulado)

        private IRepositorioTablero repo;
        private IServicioCalidadAlmacen almacen;
        private IServicioEncuestas encuestas;
        private ILectorParametrosUsuario parametros;
        private IServicioCorreoElectronico correo;
        private IRegistroCorreoResumenSemanal registro;
        private List<Exception> errores;

        [TestInitialize]
        public void Preparar()
        {
            ServicioCorreoResumenSemanal.ReiniciarAvisos();
            repo = A.Fake<IRepositorioTablero>();
            A.CallTo(() => repo.TablasExisten()).Returns(true);
            A.CallTo(() => repo.LeerSemanas(A<string>._, A<TipoDuenoKpi?>._, A<string>._, A<DateTime>._, A<DateTime>._)).Returns(SemanasPerdidaAlc());
            A.CallTo(() => repo.LeerUmbrales(A<string>._)).Returns(new List<UmbralesKpi> { UmbralPerdida() });
            almacen = A.Fake<IServicioCalidadAlmacen>();
            A.CallTo(() => almacen.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(Datos().Almacen);
            encuestas = A.Fake<IServicioEncuestas>();
            A.CallTo(() => encuestas.SombraEncendida()).Returns(true);
            A.CallTo(() => encuestas.SombraEntre(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(Datos().Encuestas);
            parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", "(defecto)", ReglaCorreoResumenSemanal.PARAMETRO_DESTINATARIO)).Returns(DESTINATARIO);
            correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            registro = A.Fake<IRegistroCorreoResumenSemanal>();
            A.CallTo(() => registro.EnviadoEl(A<DateTime>._)).Returns(null);
            errores = new List<Exception>();
        }

        private ServicioCorreoResumenSemanal Servicio() => new ServicioCorreoResumenSemanal(() => repo, () => almacen, () => encuestas, parametros, correo,
            () => registro, () => AHORA, ex => errores.Add(ex));

        [TestMethod]
        public void Ensayo_NoMandaNiApunta_YDevuelveAsuntoYHtml()
        {
            ResultadoCorreoResumenSemanalDTO r = Servicio().Ejecutar(LUNES, ensayo: true, usuario: "Prueba");

            Assert.AreEqual(EstadoCorreoResumenSemanal.Ensayo.ToString(), r.Estado);
            Assert.IsFalse(r.Enviado);
            Assert.AreEqual("Calidad: semana del 23 al 29/11/2026", r.Asunto);
            StringAssert.Contains(r.Html, "Alcobendas");
            StringAssert.Contains(r.Mensaje, DESTINATARIO);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
            A.CallTo(() => registro.Apuntar(A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Enviar_SoloAlDestinatarioConfigurado_YLoApunta()
        {
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);

            ResultadoCorreoResumenSemanalDTO r = Servicio().Ejecutar(LUNES, ensayo: false, usuario: "Prueba");

            Assert.AreEqual(EstadoCorreoResumenSemanal.Enviado.ToString(), r.Estado);
            Assert.IsTrue(r.Enviado);
            Assert.IsNotNull(enviado);
            Assert.AreEqual(DESTINATARIO, enviado.To.Single().Address);
            Assert.AreEqual(0, enviado.CC.Count);
            Assert.AreEqual(0, enviado.Bcc.Count);
            Assert.IsTrue(enviado.IsBodyHtml);
            Assert.AreEqual("Calidad: semana del 23 al 29/11/2026", enviado.Subject);
            A.CallTo(() => registro.Apuntar(LUNES, DESTINATARIO, "Calidad: semana del 23 al 29/11/2026", "Prueba")).MustHaveHappenedOnceExactly();
            A.CallTo(() => almacen.Resumen("1", LUNES, LUNES.AddDays(7))).MustHaveHappenedOnceExactly();
            A.CallTo(() => encuestas.SombraEntre("1", LUNES, LUNES.AddDays(7))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Enviar_YaMandadaEsaSemana_NoLaMandaOtraVez()
        {
            A.CallTo(() => registro.EnviadoEl(LUNES)).Returns(HOY.AddHours(7.5));

            ResultadoCorreoResumenSemanalDTO r = Servicio().Ejecutar(LUNES, ensayo: false, usuario: null);

            Assert.AreEqual(EstadoCorreoResumenSemanal.YaEnviado.ToString(), r.Estado);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
            A.CallTo(() => almacen.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Enviar_NoSePuedeComprobarSiYaSeMando_NoManda()
        {
            A.CallTo(() => registro.EnviadoEl(A<DateTime>._)).Throws(new InvalidOperationException("sin conexión"));

            ResultadoCorreoResumenSemanalDTO r = Servicio().Ejecutar(LUNES, ensayo: false, usuario: null);

            Assert.AreEqual(EstadoCorreoResumenSemanal.SinPoderComprobar.ToString(), r.Estado);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("alguien@ejemplo.test; otra@ejemplo.test")]
        [DataRow("alguien@ejemplo.test,otra@ejemplo.test")]
        [DataRow("no es una dirección")]
        public void Enviar_SinUnaDireccionValida_NoMandaNadaYAvisaUnaSolaVez(string valor)
        {
            A.CallTo(() => parametros.LeerParametroSinDefecto(A<string>._, A<string>._, A<string>._)).Returns(valor);

            ResultadoCorreoResumenSemanalDTO r1 = Servicio().Ejecutar(LUNES, ensayo: false, usuario: null);
            ResultadoCorreoResumenSemanalDTO r2 = Servicio().Ejecutar(LUNES, ensayo: false, usuario: null);

            Assert.AreEqual(EstadoCorreoResumenSemanal.SinDestinatario.ToString(), r1.Estado);
            Assert.AreEqual(EstadoCorreoResumenSemanal.SinDestinatario.ToString(), r2.Estado);
            Assert.IsNull(r1.Destinatario);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
            Assert.AreEqual(1, errores.Count(e => e.Message.Contains(ReglaCorreoResumenSemanal.PARAMETRO_DESTINATARIO)));
        }

        [TestMethod]
        public void Ensayo_SinDestinatario_EnsenaElCorreoYDiceQueNoSaldria()
        {
            A.CallTo(() => parametros.LeerParametroSinDefecto(A<string>._, A<string>._, A<string>._)).Returns(null);

            ResultadoCorreoResumenSemanalDTO r = Servicio().Ejecutar(LUNES, ensayo: true, usuario: null);

            Assert.AreEqual(EstadoCorreoResumenSemanal.Ensayo.ToString(), r.Estado);
            Assert.IsNotNull(r.Html);
            StringAssert.Contains(r.Mensaje, "no saldría");
        }

        [TestMethod]
        public void Enviar_FallaAlmacenYFaltanLasTablasDelTablero_ElCorreoSaleIgual()
        {
            A.CallTo(() => repo.TablasExisten()).Returns(false);
            A.CallTo(() => almacen.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).Throws(new TimeoutException("tiempo agotado"));
            A.CallTo(() => encuestas.SombraEntre(A<string>._, A<DateTime>._, A<DateTime>._)).Throws(new EncuestasSinTablasException());
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);

            ResultadoCorreoResumenSemanalDTO r = Servicio().Ejecutar(LUNES, ensayo: false, usuario: null);

            Assert.IsTrue(r.Enviado);
            StringAssert.Contains(enviado.Body, "No disponible: Todav&#237;a no existen dbo.CalidadKpiSemanas");
            StringAssert.Contains(enviado.Body, "No disponible: no se han podido calcular los indicadores de Almac&#233;n (tiempo agotado).");
            StringAssert.Contains(enviado.Body, "Issue914e_CalidadEncuestas.sql");
            Assert.IsTrue(errores.Any(e => e.Message.Contains("Almacén")));
        }

        [TestMethod]
        public void Job_TableroSinLaSemana_SeAplazaSinMandar_YElReintentoManda()
        {
            A.CallTo(() => repo.LeerSemanas(A<string>._, A<TipoDuenoKpi?>._, A<string>._, A<DateTime>._, A<DateTime>._))
                .Returns(SemanasPerdidaAlc().Where(s => s.Semana != LUNES).ToList());

            ResultadoCorreoResumenSemanalDTO aplazado = Servicio().Ejecutar(LUNES, ensayo: false, usuario: null, aplazarSiFaltaTablero: true);
            ResultadoCorreoResumenSemanalDTO reintento = Servicio().Ejecutar(LUNES, ensayo: false, usuario: null, aplazarSiFaltaTablero: false);

            Assert.AreEqual(EstadoCorreoResumenSemanal.Aplazado.ToString(), aplazado.Estado);
            Assert.AreEqual(EstadoCorreoResumenSemanal.Enviado.ToString(), reintento.Estado);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Enviar_ElSmtpNoLoAcepta_NoLoApunta()
        {
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(false);

            ResultadoCorreoResumenSemanalDTO r = Servicio().Ejecutar(LUNES, ensayo: false, usuario: null);

            Assert.AreEqual(EstadoCorreoResumenSemanal.FalloEnvio.ToString(), r.Estado);
            A.CallTo(() => registro.Apuntar(A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Registro_ClaveDeLaSemana()
        {
            Assert.AreEqual("Semana 2026-11-23", RegistroCorreoResumenSemanalSql.Clave(new DateTime(2026, 11, 26)));
        }
    }
}

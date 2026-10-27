using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>
    /// NestoAPI#716 (2.1): resultado del rapport (catálogo, DTO opcional, columnas que pueden no existir) y la
    /// clasificación nocturna de los comentarios con OpenAI (anonimizado, lectura de la respuesta, job). Sin llamadas
    /// reales a OpenAI ni a la base de datos.
    /// </summary>
    [TestClass]
    public class ClasificacionRapports716Tests
    {
        [TestCleanup]
        public void Limpiar()
        {
            ColumnasOpcionales.ForzarParaTests = null;
        }

        // ---------- Catálogo ----------

        [TestMethod]
        public void ResultadoRapport_CatalogoCompletoConNombres()
        {
            CollectionAssert.AreEqual(new[] { "Pedido", "PediraMasTarde", "NoNecesita", "Precio", "Competencia", "CerradoTraspaso", "SinStock", "NoDecide", "SoloInformacion", "Otro" },
                ResultadoRapport.Codigos.ToArray());
            foreach (string codigo in ResultadoRapport.Codigos)
            {
                Assert.IsTrue(ResultadoRapport.EsValido(codigo), codigo);
                Assert.IsTrue(codigo.Length <= ResultadoRapport.LONGITUD_MAXIMA, $"{codigo} no cabe en varchar(20)");
                Assert.IsFalse(string.IsNullOrWhiteSpace(ResultadoRapport.Nombres[codigo]), $"{codigo} sin nombre para mostrar");
            }
        }

        [TestMethod]
        public void ResultadoRapport_NormalizaMayusculasYTildes()
        {
            Assert.AreEqual("PediraMasTarde", ResultadoRapport.Normalizar("pedira mas tarde"));
            Assert.AreEqual("PediraMasTarde", ResultadoRapport.Normalizar("Pedirá más tarde"), "el nombre para mostrar también vale");
            Assert.AreEqual("CerradoTraspaso", ResultadoRapport.Normalizar("CERRADOTRASPASO"));
            Assert.AreEqual("SoloInformacion", ResultadoRapport.Normalizar("Solo información"));
            Assert.IsNull(ResultadoRapport.Normalizar("Inventado"));
            Assert.IsNull(ResultadoRapport.Normalizar(null));
            Assert.IsNull(ResultadoRapport.Normalizar("  "));
        }

        [TestMethod]
        public void ResultadoRapport_MotivoParaNoGuardar_SoloConValorFueraDelCatalogo()
        {
            Assert.IsNull(ResultadoRapport.MotivoParaNoGuardar(null), "opcional: los clientes viejos no lo mandan");
            Assert.IsNull(ResultadoRapport.MotivoParaNoGuardar(""));
            Assert.IsNull(ResultadoRapport.MotivoParaNoGuardar("Precio"));
            string motivo = ResultadoRapport.MotivoParaNoGuardar("Inventado");
            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "'Inventado'");
            StringAssert.Contains(motivo, "Pedido, PediraMasTarde");
        }

        // ---------- DTO opcional y columnas ausentes ----------

        [TestMethod]
        public void DTO_SinResultadoNiProximoContacto_NoHayNadaQueGuardar()
        {
            Assert.IsFalse(DatosExtraRapport.HayAlgoQueGuardar(new SeguimientoClienteDTO()), "un cliente viejo manda el DTO de siempre");
            Assert.IsFalse(DatosExtraRapport.HayAlgoQueGuardar(null));
            Assert.IsTrue(DatosExtraRapport.HayAlgoQueGuardar(new SeguimientoClienteDTO { Resultado = "Pedido" }));
            Assert.IsTrue(DatosExtraRapport.HayAlgoQueGuardar(new SeguimientoClienteDTO { ProximoContacto = new DateTime(2026, 11, 3) }));
        }

        [TestMethod]
        public void Guardar_SinLasColumnas_NoEscribeNada()
        {
            ColumnasOpcionales.ForzarParaTests = false;
            NVEntities db = A.Fake<NVEntities>();

            bool escrito = DatosExtraRapport.Guardar(db, 123, new SeguimientoClienteDTO { Resultado = "Pedido", ProximoContacto = DateTime.Today });

            Assert.IsFalse(escrito, "el script no se ha lanzado: todo sigue igual");
        }

        [TestMethod]
        public void Guardar_SinNadaQueGuardar_NiMiraLasColumnas()
        {
            ColumnasOpcionales.ForzarParaTests = true;
            Assert.IsFalse(DatosExtraRapport.Guardar(A.Fake<NVEntities>(), 123, new SeguimientoClienteDTO()));
        }

        [TestMethod]
        public void Completar_SinLasColumnas_DevuelveLosDtoTalCual()
        {
            ColumnasOpcionales.ForzarParaTests = false;
            var lista = new List<SeguimientoClienteDTO> { new SeguimientoClienteDTO { Id = 1 } };
            IList<SeguimientoClienteDTO> resultado = DatosExtraRapport.Completar(A.Fake<NVEntities>(), lista);
            Assert.AreSame(lista, resultado);
            Assert.IsNull(resultado[0].Resultado);
        }

        [TestMethod]
        public void Aplicar_VuelcaPorIdYRespetaLosQueNoTienenFila()
        {
            var dtos = new List<SeguimientoClienteDTO> { new SeguimientoClienteDTO { Id = 1 }, new SeguimientoClienteDTO { Id = 2 } };
            DatosExtraRapport.Aplicar(dtos, new[] { new DatosExtraRapport.Extra { NumOrden = 2, Resultado = "Precio   ", ProximoContacto = new DateTime(2026, 11, 5) } });
            Assert.IsNull(dtos[0].Resultado);
            Assert.AreEqual("Precio", dtos[1].Resultado, "el varchar viene con relleno");
            Assert.AreEqual(new DateTime(2026, 11, 5), dtos[1].ProximoContacto);
        }

        [TestMethod]
        public void ColumnasOpcionales_SinDbONombres_False()
        {
            Assert.IsFalse(ColumnasOpcionales.Existen(null, "SeguimientoCliente", "Resultado"));
            Assert.IsFalse(ColumnasOpcionales.Existen(A.Fake<NVEntities>(), "SeguimientoCliente"));
            Assert.IsFalse(ColumnasOpcionales.ExisteTabla(null, "SeguimientoClienteClasificacion"));
        }

        // ---------- Clasificador: anonimizado ----------

        [TestMethod]
        public void Anonimizar_TapaTelefonosYCorreos()
        {
            string texto = ClasificadorRapports.Anonimizar("Llamar a Marta al 612 34 56 78 o al +34 912345678, mail marta.lopez@salon-bella.es. Pide 3 cajas al 20%.");
            Assert.IsFalse(texto.Contains("612"), texto);
            Assert.IsFalse(texto.Contains("912345678"), texto);
            Assert.IsFalse(texto.Contains("@"), texto);
            StringAssert.Contains(texto, "[teléfono]");
            StringAssert.Contains(texto, "[correo]");
            StringAssert.Contains(texto, "3 cajas al 20%", "los números cortos (cantidades, descuentos) se dejan: clasifican");
            StringAssert.Contains(texto, "Marta", "los nombres de pila no se tocan: no identifican y dan contexto");
        }

        [TestMethod]
        public void Anonimizar_TextoVacio_Cadena()
        {
            Assert.AreEqual(string.Empty, ClasificadorRapports.Anonimizar(null));
            Assert.AreEqual(string.Empty, ClasificadorRapports.Anonimizar("   "));
        }

        [TestMethod]
        public void ConstruirMensaje_UnaLineaPorRapportSinDatosDelCliente()
        {
            var lote = new List<RapportAClasificar>
            {
                new RapportAClasificar { NumOrden = 10, Fecha = new DateTime(2026, 10, 26, 11, 30, 0), Comentarios = "Tiene stock,\r\nllamar en 2 semanas | ok" },
                new RapportAClasificar { NumOrden = 11, Fecha = new DateTime(2026, 10, 26), Comentarios = new string('x', 700) }
            };
            string mensaje = ClasificadorRapports.ConstruirMensaje(lote);
            string[] lineas = mensaje.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2, lineas.Length);
            Assert.AreEqual("10 | 2026-10-26 | Tiene stock, llamar en 2 semanas / ok", lineas[0], "sin saltos de línea ni barras, que son el separador");
            Assert.IsTrue(lineas[1].Length < 700, "el comentario largo se recorta");
            StringAssert.EndsWith(lineas[1], "…");
        }

        // ---------- Clasificador: respuesta ----------

        private static readonly List<RapportAClasificar> LOTE = new List<RapportAClasificar>
        {
            new RapportAClasificar { NumOrden = 1, Fecha = new DateTime(2026, 10, 26), Comentarios = "a" },
            new RapportAClasificar { NumOrden = 2, Fecha = new DateTime(2026, 10, 26), Comentarios = "b" },
            new RapportAClasificar { NumOrden = 3, Fecha = new DateTime(2026, 10, 26), Comentarios = "c" }
        };

        [TestMethod]
        public void ParsearRespuesta_LeeElArrayAunqueVengaConMarcasDeCodigo()
        {
            string respuesta = "```json\n[{\"id\":1,\"resultado\":\"PediraMasTarde\",\"proximoContacto\":\"2026-11-09\",\"confianza\":0.9}," +
                "{\"id\":2,\"resultado\":\"Otro\",\"proximoContacto\":null,\"confianza\":0.4}]\n```";
            List<ClasificacionRapport> filas = ClasificadorRapports.ParsearRespuesta(respuesta, LOTE);
            Assert.AreEqual(2, filas.Count);
            Assert.AreEqual("PediraMasTarde", filas[0].Resultado);
            Assert.AreEqual(new DateTime(2026, 11, 9), filas[0].ProximoContacto);
            Assert.AreEqual(0.9m, filas[0].Confianza);
            Assert.AreEqual(ClasificadorRapports.MODELO, filas[0].Modelo);
            Assert.IsNull(filas[1].ProximoContacto);
        }

        [TestMethod]
        public void ParsearRespuesta_DescartaIdsAjenosResultadosInventadosYDuplicados()
        {
            string respuesta = "[{\"id\":1,\"resultado\":\"Inventado\",\"confianza\":1}," +
                "{\"id\":99,\"resultado\":\"Pedido\",\"confianza\":1}," +
                "{\"id\":2,\"resultado\":\"precio\",\"confianza\":85}," +
                "{\"id\":2,\"resultado\":\"Pedido\",\"confianza\":1}," +
                "{\"id\":\"3\",\"resultado\":\"No decide\",\"confianza\":\"0.7\"}]";
            List<ClasificacionRapport> filas = ClasificadorRapports.ParsearRespuesta(respuesta, LOTE);
            Assert.AreEqual(2, filas.Count, "fuera el inventado, el id ajeno y el duplicado");
            Assert.AreEqual("Precio", filas[0].Resultado, "se normaliza al código del catálogo");
            Assert.AreEqual(0.85m, filas[0].Confianza, "«85» se entiende como 85 %");
            Assert.AreEqual(3, filas[1].NumOrden, "id como texto también vale");
            Assert.AreEqual("NoDecide", filas[1].Resultado);
            Assert.AreEqual(0.7m, filas[1].Confianza);
        }

        [TestMethod]
        public void ParsearRespuesta_FechasImposibles_SeGuardaElResultadoSinFecha()
        {
            string respuesta = "[{\"id\":1,\"resultado\":\"PediraMasTarde\",\"proximoContacto\":\"2020-01-01\",\"confianza\":0.9}," +
                "{\"id\":2,\"resultado\":\"PediraMasTarde\",\"proximoContacto\":\"2031-01-01\",\"confianza\":0.9}," +
                "{\"id\":3,\"resultado\":\"PediraMasTarde\",\"proximoContacto\":\"la semana que viene\",\"confianza\":0.9}]";
            List<ClasificacionRapport> filas = ClasificadorRapports.ParsearRespuesta(respuesta, LOTE);
            Assert.AreEqual(3, filas.Count);
            Assert.IsTrue(filas.All(f => f.ProximoContacto == null), "anterior al rapport, a más de dos años o sin formato: null");
        }

        [TestMethod]
        public void ParsearRespuesta_SinJson_ListaVacia()
        {
            Assert.AreEqual(0, ClasificadorRapports.ParsearRespuesta("Lo siento, no puedo ayudarte con eso.", LOTE).Count);
            Assert.AreEqual(0, ClasificadorRapports.ParsearRespuesta(null, LOTE).Count);
            Assert.AreEqual(0, ClasificadorRapports.ParsearRespuesta("[{\"id\":1,", LOTE).Count, "JSON roto");
        }

        [TestMethod]
        public void Lotes_DeVeinte()
        {
            List<RapportAClasificar> rapports = Enumerable.Range(1, 45).Select(i => new RapportAClasificar { NumOrden = i }).ToList();
            List<List<RapportAClasificar>> lotes = ClasificadorRapports.Lotes(rapports).ToList();
            CollectionAssert.AreEqual(new[] { 20, 20, 5 }, lotes.Select(l => l.Count).ToArray());
            Assert.AreEqual(0, ClasificadorRapports.Lotes(new List<RapportAClasificar>()).Count());
        }

        // ---------- Job ----------

        [TestMethod]
        public void Job_IdYCron_DiarioDeNoche()
        {
            Assert.AreEqual("clasificar-rapports-nocturno", ClasificacionRapportsJobsService.ID_JOB);
            Assert.AreEqual("15 23 * * *", ClasificacionRapportsJobsService.CRON);
            Assert.IsTrue(ClasificacionRapportsJobsService.EsHorarioPermitido(new DateTime(2026, 10, 27, 23, 15, 0)));
            Assert.IsTrue(ClasificacionRapportsJobsService.EsHorarioPermitido(new DateTime(2026, 10, 28, 2, 0, 0)));
            Assert.IsFalse(ClasificacionRapportsJobsService.EsHorarioPermitido(new DateTime(2026, 10, 28, 10, 0, 0)), "Hangfire lanza la ejecución perdida tras un despliegue de día: se espera a la noche");
            Assert.IsFalse(ClasificacionRapportsJobsService.EsHorarioPermitido(new DateTime(2026, 10, 28, 20, 59, 0)));
        }

        [TestMethod]
        public void Job_VentanaDiaria_AyerYHoy()
        {
            (DateTime desde, DateTime hasta) = ClasificacionRapportsJobsService.VentanaDiaria(new DateTime(2026, 10, 27, 23, 15, 0));
            Assert.AreEqual(new DateTime(2026, 10, 26), desde, "ayer, por si el job de ayer falló");
            Assert.AreEqual(new DateTime(2026, 10, 28), hasta);
        }

        [TestMethod]
        public void Opciones_PorDefecto_ActivoYSinBackfill()
        {
            var opciones = new OpcionesClasificacionRapports();
            Assert.IsTrue(opciones.Activo);
            Assert.IsFalse(opciones.Backfill, "el relleno de 2024-2026 va detrás de un interruptor apagado");
            Assert.AreEqual(new DateTime(2024, 1, 1), opciones.BackfillDesde);
            Assert.AreEqual(3000, opciones.BackfillPorNoche);
        }

        private static readonly DateTime NOCHE = new DateTime(2026, 10, 27, 23, 15, 0);

        private static (ClasificacionRapportsJobsService job, IRepositorioClasificacionRapports repo, IServicioOpenAI openAI, List<ClasificacionRapport> guardadas)
            Montar(bool tabla = true, bool conOpenAI = true, params RapportAClasificar[] pendientes)
        {
            IRepositorioClasificacionRapports repo = A.Fake<IRepositorioClasificacionRapports>();
            A.CallTo(() => repo.TablaDisponible()).Returns(tabla);
            A.CallTo(() => repo.LeerPendientes(A<DateTime>._, A<DateTime>._, A<int>._)).Returns(pendientes.ToList());
            var guardadas = new List<ClasificacionRapport>();
            A.CallTo(() => repo.Guardar(A<IEnumerable<ClasificacionRapport>>._)).Invokes((IEnumerable<ClasificacionRapport> c) => guardadas.AddRange(c));
            IServicioOpenAI openAI = A.Fake<IServicioOpenAI>();
            var job = new ClasificacionRapportsJobsService(repo, () => conOpenAI ? openAI : null);
            return (job, repo, openAI, guardadas);
        }

        private static RapportAClasificar Rapport(int id)
        {
            return new RapportAClasificar { NumOrden = id, Fecha = new DateTime(2026, 10, 27), Comentarios = "Comentario " + id };
        }

        [TestMethod]
        public async Task Job_Apagado_NoHaceNada()
        {
            (ClasificacionRapportsJobsService job, IRepositorioClasificacionRapports repo, IServicioOpenAI openAI, _) = Montar(pendientes: Rapport(1));
            ResultadoClasificacionRapports r = await job.Ejecutar(NOCHE, new OpcionesClasificacionRapports { Activo = false });
            StringAssert.Contains(r.MotivoSalida, "apagado");
            A.CallTo(() => repo.LeerPendientes(A<DateTime>._, A<DateTime>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_DeDia_NoHaceNada()
        {
            (ClasificacionRapportsJobsService job, _, IServicioOpenAI openAI, _) = Montar(pendientes: Rapport(1));
            ResultadoClasificacionRapports r = await job.Ejecutar(new DateTime(2026, 10, 28, 10, 0, 0), new OpcionesClasificacionRapports());
            StringAssert.Contains(r.MotivoSalida, "de noche");
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_SinTablaOSinClave_NoHaceNada()
        {
            (ClasificacionRapportsJobsService sinTabla, _, IServicioOpenAI openAI1, _) = Montar(tabla: false, pendientes: Rapport(1));
            StringAssert.Contains((await sinTabla.Ejecutar(NOCHE, new OpcionesClasificacionRapports())).MotivoSalida, "Issue716");
            A.CallTo(() => openAI1.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).MustNotHaveHappened();

            (ClasificacionRapportsJobsService sinClave, IRepositorioClasificacionRapports repo2, _, _) = Montar(conOpenAI: false, pendientes: Rapport(1));
            StringAssert.Contains((await sinClave.Ejecutar(NOCHE, new OpcionesClasificacionRapports())).MotivoSalida, "OpenAIKey");
            A.CallTo(() => repo2.LeerPendientes(A<DateTime>._, A<DateTime>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_ClasificaYGuarda_SoloConElTextoDelComentario()
        {
            (ClasificacionRapportsJobsService job, IRepositorioClasificacionRapports repo, IServicioOpenAI openAI, List<ClasificacionRapport> guardadas) = Montar(pendientes: new[] { Rapport(1), Rapport(2) });
            string mensajeEnviado = null;
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .Invokes((string sistema, string usuario, int t, double temp, string modelo) => mensajeEnviado = usuario)
                .Returns(Task.FromResult("[{\"id\":1,\"resultado\":\"Pedido\",\"confianza\":0.95},{\"id\":2,\"resultado\":\"NoNecesita\",\"confianza\":0.6}]"));

            ResultadoClasificacionRapports r = await job.Ejecutar(NOCHE, new OpcionesClasificacionRapports());

            Assert.IsNull(r.MotivoSalida);
            Assert.AreEqual(2, r.Leidos);
            Assert.AreEqual(1, r.Lotes);
            Assert.AreEqual(2, r.Guardados);
            Assert.AreEqual(0, r.LotesFallidos);
            CollectionAssert.AreEqual(new[] { "Pedido", "NoNecesita" }, guardadas.Select(g => g.Resultado).ToArray());
            Assert.AreEqual("1 | 2026-10-27 | Comentario 1\r\n2 | 2026-10-27 | Comentario 2\r\n", mensajeEnviado, "ni cliente ni vendedor: solo id, fecha y texto");
            A.CallTo(() => openAI.GenerarContenidoAsync(ClasificadorRapports.PROMPT_SISTEMA, A<string>._, ClasificadorRapports.MAX_TOKENS_RESPUESTA, 0.0, ClasificadorRapports.MODELO)).MustHaveHappenedOnceExactly();
            // Sin backfill: una sola lectura, la de ayer y hoy
            A.CallTo(() => repo.LeerPendientes(new DateTime(2026, 10, 26), new DateTime(2026, 10, 28), 1000)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.LeerPendientes(A<DateTime>._, A<DateTime>._, A<int>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Job_ConBackfill_LeeAdemasHaciaAtrasPorTandas()
        {
            (ClasificacionRapportsJobsService job, IRepositorioClasificacionRapports repo, IServicioOpenAI openAI, _) = Montar();
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).Returns(Task.FromResult("[]"));

            _ = await job.Ejecutar(NOCHE, new OpcionesClasificacionRapports { Backfill = true, BackfillDesde = new DateTime(2024, 1, 1), BackfillPorNoche = 500 });

            A.CallTo(() => repo.LeerPendientes(new DateTime(2026, 10, 26), new DateTime(2026, 10, 28), 1000)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.LeerPendientes(new DateTime(2024, 1, 1), new DateTime(2026, 10, 26), 500)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Job_TresLotesSeguidosSinRespuestaValida_SePara()
        {
            RapportAClasificar[] muchos = Enumerable.Range(1, 200).Select(Rapport).ToArray();
            (ClasificacionRapportsJobsService job, _, IServicioOpenAI openAI, List<ClasificacionRapport> guardadas) = Montar(pendientes: muchos);
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).Returns(Task.FromResult<string>(null));

            ResultadoClasificacionRapports r = await job.Ejecutar(NOCHE, new OpcionesClasificacionRapports());

            Assert.AreEqual(ClasificacionRapportsJobsService.MAXIMO_FALLOS_SEGUIDOS, r.Lotes, "no se insiste con OpenAI caído: 10 lotes serían 10 fallos");
            Assert.AreEqual(ClasificacionRapportsJobsService.MAXIMO_FALLOS_SEGUIDOS, r.LotesFallidos);
            Assert.AreEqual(0, guardadas.Count);
        }

        [TestMethod]
        public async Task Job_UnLoteQueFalla_NoParaLosDemas()
        {
            RapportAClasificar[] cuarenta = Enumerable.Range(1, 40).Select(Rapport).ToArray();
            (ClasificacionRapportsJobsService job, _, IServicioOpenAI openAI, List<ClasificacionRapport> guardadas) = Montar(pendientes: cuarenta);
            int llamada = 0;
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .ReturnsLazily(() => ++llamada == 1 ? throw new InvalidOperationException("timeout") : Task.FromResult("[{\"id\":21,\"resultado\":\"Otro\",\"confianza\":0.5}]"));

            ResultadoClasificacionRapports r = await job.Ejecutar(NOCHE, new OpcionesClasificacionRapports());

            Assert.AreEqual(2, r.Lotes);
            Assert.AreEqual(1, r.LotesFallidos);
            Assert.AreEqual(1, r.Guardados);
            Assert.AreEqual(21, guardadas.Single().NumOrden);
        }
    }
}

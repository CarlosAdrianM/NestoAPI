using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Almacen;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914 (27/11/26): el hueco de cada lectura del recorrido y «falta que sí estaba».</summary>
    [TestClass]
    public class FaltaQueSiEstabaTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 12, 1, 9, 0, 0);

        private static FaltaConHuecoAlmacen Falta(int picking = 100, string hueco = "001/002/004", int faltas = 1) =>
            new FaltaConHuecoAlmacen { Picking = picking, Producto = "41262", Hueco = hueco, Usuario = "Pedro", Faltas = faltas, Fecha = DIA };

        private static CogidaConHuecoAlmacen Cogida(int numero = 101, string hueco = "001002004", double horas = 1, string tipo = "PICK") =>
            new CogidaConHuecoAlmacen { TipoOrigen = tipo, NumeroOrigen = numero, Producto = "41262", Hueco = hueco, Usuario = "Alfredo", Fecha = DIA.AddHours(horas), Cantidad = 1 };

        // ------------------------------------------------------------ LA REGLA

        [TestMethod]
        public void CogidoDelMismoHuecoEnOtraSalidaPocoDespues_FaltaQueSiEstaba_ConOSinBarras()
        {
            FaltaQueSiEstabaAlmacen r = ReglaFaltaQueSiEstaba.Evaluar(new[] { Falta() }, new[] { Cogida() }).Single();

            Assert.AreEqual(101, r.Cogida.NumeroOrigen);
            Assert.AreEqual(1, ReglaFaltaQueSiEstaba.Evaluar(new[] { Falta() }, new[] { Cogida(tipo: "REPO", numero: 100) }).Count, "Una reposición con el mismo número es otra salida");
        }

        [TestMethod]
        public void NoCuentan_OtroHueco_ElMismoPicking_FueraDeLaVentana_Antes_OFaltaDeshecha()
        {
            Assert.AreEqual(0, ReglaFaltaQueSiEstaba.Evaluar(new[] { Falta() }, new[] { Cogida(hueco: "001/002/005") }).Count);
            Assert.AreEqual(0, ReglaFaltaQueSiEstaba.Evaluar(new[] { Falta() }, new[] { Cogida(numero: 100) }).Count, "Eso es FaltaDeshechaPorOtro");
            Assert.AreEqual(0, ReglaFaltaQueSiEstaba.Evaluar(new[] { Falta() }, new[] { Cogida(horas: ReglaFaltaQueSiEstaba.HORAS + 0.1) }).Count);
            Assert.AreEqual(0, ReglaFaltaQueSiEstaba.Evaluar(new[] { Falta() }, new[] { Cogida(horas: -1) }).Count);
            Assert.AreEqual(0, ReglaFaltaQueSiEstaba.Evaluar(new[] { Falta(faltas: 0) }, new[] { Cogida() }).Count);
            Assert.AreEqual(0, ReglaFaltaQueSiEstaba.Evaluar(new[] { Falta(hueco: "no es un hueco") }, new[] { Cogida() }).Count);
        }

        [TestMethod]
        public void EnLaRegla_SoloConLaColumnaYDesdeLaPrimeraLecturaConHueco_ImputadaAQuienDioLaFalta()
        {
            var lectura = new LecturaCalidadAlmacen();
            lectura.Pickings.Add(new PickingCalidadAlmacen { Picking = 100, Fecha = DIA });
            lectura.LecturasPicking.Add(new LecturaPickingCalidadAlmacen { Picking = 100, Producto = "41262", Usuario = "Pedro", Faltas = 1, Ultima = DIA });
            lectura.FaltasConHueco.Add(Falta());
            lectura.CogidasConHueco.Add(Cogida());

            ParadaPreparadaAlmacen sinColumna = ReglaCalidadAlmacen.Evaluar(lectura, DIA).Paradas.Single();
            Assert.IsTrue(sinColumna.NoEvaluados.Contains(IndicadorAlmacen.FaltaQueSiEstaba));

            lectura.HayColumnaHueco = true;
            lectura.PrimeraLecturaConHueco = DIA.AddMinutes(-5);
            ParadaPreparadaAlmacen parada = ReglaCalidadAlmacen.Evaluar(lectura, DIA).Paradas.Single();

            Assert.IsTrue(parada.Indicadores.Contains(IndicadorAlmacen.FaltaQueSiEstaba));
            Assert.AreEqual("Pedro", parada.Imputado(IndicadorAlmacen.FaltaQueSiEstaba));
            Assert.AreEqual(101, parada.FaltasQueSiEstaban.Single().Cogida.NumeroOrigen);
            Assert.IsFalse(InfoIndicadoresAlmacen.EsError(IndicadorAlmacen.FaltaQueSiEstaba));

            ResumenCalidadAlmacenDTO resumen = ServicioCalidadAlmacen.Resumir(new CalculoCalidadAlmacen
            {
                Empresa = "1", Lectura = lectura, Evaluacion = ReglaCalidadAlmacen.Evaluar(lectura, DIA)
            });
            Assert.IsFalse(resumen.SinRastro.Any(s => s.Indicador == "FaltaQueSiEstaba"), "Con la columna deja de estar sin rastro");
            Assert.AreEqual(1, resumen.Indicadores.Single(i => i.Indicador == "FaltaQueSiEstaba").Con);
        }

        // ------------------------------------------------------------ Lector

        [TestMethod]
        public void Lote_ConColumnaLeeElHuecoYSinElla_NoLoNombra()
        {
            StringAssert.Contains(LectorCalidadAlmacenSql.Lote(true, true, true), "e.Hueco IS NOT NULL");
            Assert.IsFalse(LectorCalidadAlmacenSql.Lote(true, true, false).Contains("Hueco"));
            Assert.IsFalse(LectorCalidadAlmacenSql.Lote(true, true).Contains("Hueco"));
        }

        [TestMethod]
        public void LeerHueco_LosTresResultados()
        {
            var ds = new DataSet();
            var f = new DataTable("Faltas");
            foreach (var c in new (string, Type)[] { ("Picking", typeof(int)), ("Producto", typeof(string)), ("Hueco", typeof(string)), ("Usuario", typeof(string)),
                ("Faltas", typeof(int)), ("Fecha", typeof(DateTime)) })
            {
                _ = f.Columns.Add(c.Item1, c.Item2);
            }
            _ = f.Rows.Add(100, "41262", "001/002/004", "Pedro", 1, DIA);
            ds.Tables.Add(f);
            var g = new DataTable("Cogidas");
            foreach (var c in new (string, Type)[] { ("TipoOrigen", typeof(string)), ("NumeroOrigen", typeof(int)), ("Producto", typeof(string)), ("Hueco", typeof(string)),
                ("Usuario", typeof(string)), ("FechaEscaneo", typeof(DateTime)), ("Cantidad", typeof(int)) })
            {
                _ = g.Columns.Add(c.Item1, c.Item2);
            }
            _ = g.Rows.Add("REPO", 81029, "41262", "001/002/004", "Alfredo", DIA.AddHours(1), 2);
            ds.Tables.Add(g);
            var p = new DataTable("Primera");
            _ = p.Columns.Add("Primera", typeof(DateTime));
            _ = p.Rows.Add(DIA.AddHours(-1));
            ds.Tables.Add(p);
            var lectura = new LecturaCalidadAlmacen();

            using (DataTableReader r = ds.CreateDataReader())
            {
                LectorCalidadAlmacenSql.LeerHueco(r, lectura);
            }

            Assert.AreEqual("001/002/004", lectura.FaltasConHueco.Single().Hueco);
            Assert.AreEqual(81029, lectura.CogidasConHueco.Single().NumeroOrigen);
            Assert.AreEqual(DIA.AddHours(-1), lectura.PrimeraLecturaConHueco);
        }

        // ------------------------------------------------------------ Guardar el hueco de la lectura

        private static EscaneoAlmacenDTO Escaneo(string hueco, int? pendiente = null) => new EscaneoAlmacenDTO
        {
            IdCliente = Guid.NewGuid(), TipoOrigen = "PICK", NumeroOrigen = 100, Producto = "41262", Fase = "PICK", Cantidad = 1, Metodo = "SCAN",
            FechaEscaneo = DIA, Hueco = hueco, PendienteParada = pendiente, TotalParada = pendiente
        };

        [TestMethod]
        public void Insertar_ConColumnaYHueco_LoGuardaRecortado_ConYSinParada()
        {
            (string sql, SqlParameter[] parametros) = RepositorioPreparacionAlmacen.ComandoInsertarEscaneo("1", Escaneo(" 001/002/004 "), "Pedro", false, true);
            Assert.AreEqual(RepositorioPreparacionAlmacen.SQL_INSERTAR_ESCANEO_CON_HUECO, sql);
            Assert.AreEqual("001/002/004", parametros.Single(x => x.ParameterName == "@pHueco").Value);

            (sql, parametros) = RepositorioPreparacionAlmacen.ComandoInsertarEscaneo("1", Escaneo(new string('9', 30), 3), "Pedro", true, true);
            Assert.AreEqual(RepositorioPreparacionAlmacen.SQL_INSERTAR_ESCANEO_CON_PARADA_Y_HUECO, sql);
            Assert.AreEqual(RepositorioPreparacionAlmacen.LONGITUD_HUECO, ((string)parametros.Single(x => x.ParameterName == "@pHueco").Value).Length);
            Assert.AreEqual(18, parametros.Length);
        }

        [TestMethod]
        public void Insertar_SinColumnaOSinHueco_ElInsertDeSiempre()
        {
            Assert.AreEqual(RepositorioPreparacionAlmacen.SQL_INSERTAR_ESCANEO,
                RepositorioPreparacionAlmacen.ComandoInsertarEscaneo("1", Escaneo("001/002/004"), "Pedro", false, false).Sql);
            Assert.AreEqual(RepositorioPreparacionAlmacen.SQL_INSERTAR_ESCANEO,
                RepositorioPreparacionAlmacen.ComandoInsertarEscaneo("1", Escaneo(null), "Pedro", false, true).Sql);
            Assert.AreEqual(RepositorioPreparacionAlmacen.SQL_INSERTAR_ESCANEO_CON_PARADA,
                RepositorioPreparacionAlmacen.ComandoInsertarEscaneo("1", Escaneo("", 3), "Pedro", true, true).Sql);
        }

        [TestMethod]
        public async Task HayColumnaHueco_SiNoEstaVuelveAMirarPasadoUnRato_YSiEstaNoPreguntaMas()
        {
            RepositorioPreparacionAlmacen.OlvidarColumnaHueco();
            int preguntas = 0;
            Assert.IsFalse(await RepositorioPreparacionAlmacen.HayColumnaHueco(() => { preguntas++; return Task.FromResult(false); }));
            Assert.IsFalse(await RepositorioPreparacionAlmacen.HayColumnaHueco(() => { preguntas++; return Task.FromResult(true); }), "Dentro de los 5 minutos no vuelve a mirar");
            Assert.AreEqual(1, preguntas);
            RepositorioPreparacionAlmacen.OlvidarColumnaHueco();
            Assert.IsTrue(await RepositorioPreparacionAlmacen.HayColumnaHueco(() => Task.FromResult(true)));
            Assert.IsTrue(await RepositorioPreparacionAlmacen.HayColumnaHueco(() => throw new InvalidOperationException("no debe preguntar")));
            RepositorioPreparacionAlmacen.OlvidarColumnaHueco();
        }

        [TestMethod]
        public void Escaneo_ElHuecoEsOpcionalEnElJson()
        {
            EscaneoAlmacenDTO viejo = JsonConvert.DeserializeObject<EscaneoAlmacenDTO>("{\"Producto\":\"41262\",\"Fase\":\"PICK\",\"Cantidad\":1}");
            Assert.IsNull(viejo.Hueco);
            Assert.AreEqual("001/002/004", JsonConvert.DeserializeObject<EscaneoAlmacenDTO>("{\"Hueco\":\"001/002/004\"}").Hueco);
        }
    }
}

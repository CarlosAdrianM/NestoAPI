using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#713 (incidencia 785, picking 99941, 27/10/26): Pedro tecleó 42 unidades de la 45958 cuando el picking pedía
    /// 10, y Ariadna no deja teclear más de lo que pide la parada. Doble seguro: la cantidad de una parada nunca supera lo
    /// que piden sus líneas (con aviso en ELMAH una vez por salida y producto) y cada escaneo guarda lo que pedía la parada.
    /// </summary>
    [TestClass]
    public class RecorteParadasRecogidaTests
    {
        private const string EMPRESA = "1";

        [TestInitialize]
        public void Inicializar()
        {
            RecorteParadasRecogida.OlvidarAvisos();
            RepositorioPreparacionAlmacen.OlvidarColumnasParada();
        }

        private static FilaParadaRecogida Parada(string producto, int cantidad, int? reservado, int pendienteLineas, string pasillo = null)
        {
            return new FilaParadaRecogida
            {
                Producto = producto,
                Cantidad = cantidad,
                Reservado = reservado,
                PendienteLineas = pendienteLineas,
                Pasillo = pasillo,
                Fila = pasillo == null ? null : "007",
                Columna = pasillo == null ? null : "014"
            };
        }

        [TestMethod]
        public void Parada_ConMasReservasQueLineas_SeRecortaALasLineasYSeAvisa()
        {
            var avisos = new List<Exception>();

            List<LineaPickingAlmacenDTO> paradas = RecorteParadasRecogida.Aplicar(EMPRESA, "PICK", 99941,
                new[] { Parada("45958", 42, 42, 10, "001") }, avisos.Add);

            Assert.AreEqual(1, paradas.Count);
            Assert.AreEqual(10, paradas[0].Cantidad, "La parada no puede pedir más de lo que piden sus líneas");
            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "NestoAPI#713");
            StringAssert.Contains(avisos[0].Message, "99941");
            StringAssert.Contains(avisos[0].Message, "45958");
            StringAssert.Contains(avisos[0].Message, "001/007/014");
            StringAssert.Contains(avisos[0].Message, "reservas 42");
            StringAssert.Contains(avisos[0].Message, "líneas 10");
        }

        [TestMethod]
        public void Parada_QueCuadra_NoCambiaNiAvisa()
        {
            var avisos = new List<Exception>();

            // 99942 hoy: 22 reservadas (17 + 3 + 2) para 22 de las líneas (20 + 2)
            List<LineaPickingAlmacenDTO> paradas = RecorteParadasRecogida.Aplicar(EMPRESA, "PICK", 99942,
                new[] { Parada("45958", 22, 22, 22), Parada("A", 2, 2, 5, "001") }, avisos.Add);

            CollectionAssert.AreEqual(new[] { 22, 2 }, paradas.Select(p => p.Cantidad).ToArray());
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void Parada_SinReserva_SaleComoHoyConSusLineas()
        {
            var avisos = new List<Exception>();

            List<LineaPickingAlmacenDTO> paradas = RecorteParadasRecogida.Aplicar(EMPRESA, "PICK", 99943,
                new[] { Parada("B", 4, null, 4) }, avisos.Add);

            Assert.AreEqual(4, paradas.Single().Cantidad);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void Parada_ConReservasYLineasSinNadaPendiente_Desaparece()
        {
            var avisos = new List<Exception>();

            List<LineaPickingAlmacenDTO> paradas = RecorteParadasRecogida.Aplicar(EMPRESA, "PICK", 99944,
                new[] { Parada("C", 3, 3, 0, "002"), Parada("D", 1, null, 1) }, avisos.Add);

            Assert.AreEqual("D", paradas.Single().Producto);
            Assert.AreEqual(1, avisos.Count);
        }

        [TestMethod]
        public void Aviso_UnaSolaVezPorSalidaYProducto()
        {
            var avisos = new List<Exception>();

            for (int i = 0; i < 3; i++)
            {
                // Cada lectura del recorrido trae otra vez la misma parada mal: un solo aviso, con los dos huecos juntos
                RecorteParadasRecogida.Aplicar(EMPRESA, "PICK", 99941,
                    new[] { Parada("45958", 42, 42, 10, "001"), Parada("45958", 30, 30, 10, "002") }, avisos.Add);
            }
            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "001/007/014");
            StringAssert.Contains(avisos[0].Message, "002/007/014");

            // Otro producto del mismo picking, u otro picking: aviso nuevo
            RecorteParadasRecogida.Aplicar(EMPRESA, "PICK", 99941, new[] { Parada("OTRO", 5, 5, 1, "001") }, avisos.Add);
            RecorteParadasRecogida.Aplicar(EMPRESA, "PICK", 99942, new[] { Parada("45958", 42, 42, 22, "001") }, avisos.Add);
            Assert.AreEqual(3, avisos.Count);
        }

        [TestMethod]
        public void Aviso_SiFallaElRegistro_LaRecogidaSigue()
        {
            List<LineaPickingAlmacenDTO> paradas = RecorteParadasRecogida.Aplicar(EMPRESA, "PICK", 99941,
                new[] { Parada("45958", 42, 42, 10, "001") }, _ => throw new InvalidOperationException("sin ELMAH"));

            Assert.AreEqual(10, paradas.Single().Cantidad);
        }

        [TestMethod]
        public void Reposicion_ElHuecoConMasRegistroQueSalida_SeRecorta()
        {
            var avisos = new List<Exception>();

            List<LineaPickingAlmacenDTO> paradas = RecorteParadasRecogida.Aplicar(EMPRESA, "REPO", 81050,
                new[] { Parada("E", 12, 12, 5, "003"), Parada("F", 2, null, 2) }, avisos.Add);

            CollectionAssert.AreEqual(new[] { 5, 2 }, paradas.Select(p => p.Cantidad).ToArray());
            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "REPO 81050");
        }

        [TestMethod]
        public void FilaParada_NoSacaEnElJsonLosNumerosDelRecorte()
        {
            string json = JsonConvert.SerializeObject(Parada("A", 3, 3, 3, "001"));

            Assert.IsFalse(json.Contains("Reservado"));
            Assert.IsFalse(json.Contains("PendienteLineas"));
            StringAssert.Contains(json, "\"Cantidad\":3");
        }

        [TestMethod]
        public void SqlPicking_SumaLasLineasUnaVezPorLinea_NoSobreElJoinConLasReservas()
        {
            // Pedido 929262 (picking 99942): la línea 329365300 (20) tiene dos reservas (17 + 3). Sumando las líneas sobre
            // el JOIN con Ubicaciones salían 20 + 20 + 2 = 42 en vez de 22.
            string sql = RepositorioPreparacionAlmacen.SQL_LINEAS_PICKING;

            StringAssert.Contains(sql, "GROUP BY x.Pedido, x.Linea, u.Pasillo, u.Fila, u.Columna",
                "Las reservas se agrupan por línea y hueco antes de juntarlas con las líneas");
            StringAssert.Contains(sql, "LEFT JOIN Reservas r ON r.Pedido = x.Pedido AND r.Linea = x.Linea");
            StringAssert.Contains(sql, "SUM(x.Pendiente) AS Pendiente");
            Assert.IsFalse(sql.Contains("SUM(l.Cantidad - l.Recoger)"), "Nada de sumar las líneas sobre el JOIN con Ubicaciones");
            StringAssert.Contains(sql, "AS Reservado");
            StringAssert.Contains(sql, "AS PendienteLineas");
            StringAssert.Contains(sql, "CAST(ABS(ISNULL(t.Reservas, t.Pendiente)) AS int) AS Cantidad");
        }

        [TestMethod]
        public void SqlReposicion_DevuelveLoDelRegistroYLoQueSale()
        {
            string sql = RepositorioPreparacionAlmacen.SQL_LINEAS_REPOSICION_SALIDA;

            StringAssert.Contains(sql, "x.Cantidad AS Salida");
            StringAssert.Contains(sql, "AS Reservado");
            StringAssert.Contains(sql, "CAST(ISNULL(MAX(t.Salida), SUM(t.Cantidad)) AS int) AS PendienteLineas");
        }

        private static EscaneoAlmacenDTO Escaneo(int? pendiente, int? total)
        {
            return new EscaneoAlmacenDTO
            {
                IdCliente = Guid.NewGuid(),
                Picking = 99941,
                Producto = " 45958 ",
                Fase = "pick",
                Cantidad = 3,
                Metodo = "tecla",
                Dispositivo = "Redmi 7A",
                FechaEscaneo = new DateTime(2026, 10, 27, 8, 46, 0),
                PendienteParada = pendiente,
                TotalParada = total
            };
        }

        [TestMethod]
        public void Escaneo_ConLasColumnas_GuardaLoQuePediaLaParada()
        {
            (string sql, SqlParameter[] parametros) = RepositorioPreparacionAlmacen.ComandoInsertarEscaneo(EMPRESA, Escaneo(10, 10), "Pedro", true);

            Assert.AreEqual(RepositorioPreparacionAlmacen.SQL_INSERTAR_ESCANEO_CON_PARADA, sql);
            StringAssert.Contains(sql, "PendienteParada, TotalParada");
            Assert.AreEqual(17, parametros.Length);
            Assert.AreEqual(10, parametros.Single(p => p.ParameterName == "@p15").Value);
            Assert.AreEqual(10, parametros.Single(p => p.ParameterName == "@p16").Value);
            Assert.AreEqual("45958", parametros.Single(p => p.ParameterName == "@p5").Value);
        }

        [TestMethod]
        public void Escaneo_SinLosCamposNuevos_SeGuardaConNulos()
        {
            (string sql, SqlParameter[] parametros) = RepositorioPreparacionAlmacen.ComandoInsertarEscaneo(EMPRESA, Escaneo(null, null), "Pedro", true);

            Assert.AreEqual(RepositorioPreparacionAlmacen.SQL_INSERTAR_ESCANEO_CON_PARADA, sql);
            Assert.AreEqual(DBNull.Value, parametros.Single(p => p.ParameterName == "@p15").Value);
            Assert.AreEqual(DBNull.Value, parametros.Single(p => p.ParameterName == "@p16").Value);
        }

        [TestMethod]
        public void Escaneo_SinLasColumnasEnLaTabla_SeGuardaComoAntes()
        {
            (string sql, SqlParameter[] parametros) = RepositorioPreparacionAlmacen.ComandoInsertarEscaneo(EMPRESA, Escaneo(10, 10), "Pedro", false);

            Assert.AreEqual(RepositorioPreparacionAlmacen.SQL_INSERTAR_ESCANEO, sql);
            Assert.IsFalse(sql.Contains("PendienteParada"));
            Assert.AreEqual(15, parametros.Length);
        }

        [TestMethod]
        public async Task Columnas_SiNoEstan_NoSeVuelveAPreguntarEnCadaEscaneo()
        {
            int preguntas = 0;
            Func<Task<bool>> noEstan = () => { preguntas++; return Task.FromResult(false); };

            Assert.IsFalse(await RepositorioPreparacionAlmacen.HayColumnasParada(noEstan));
            Assert.IsFalse(await RepositorioPreparacionAlmacen.HayColumnasParada(noEstan));
            Assert.AreEqual(1, preguntas, "Sin columnas se vuelve a mirar como mucho cada 5 minutos");
        }

        [TestMethod]
        public async Task Columnas_UnaVezQueEstan_SeQuedaSabido()
        {
            int preguntas = 0;
            Assert.IsTrue(await RepositorioPreparacionAlmacen.HayColumnasParada(() => { preguntas++; return Task.FromResult(true); }));
            Assert.IsTrue(await RepositorioPreparacionAlmacen.HayColumnasParada(() => { preguntas++; return Task.FromResult(false); }));
            Assert.AreEqual(1, preguntas);
        }

        [TestMethod]
        public async Task Columnas_SiFallaLaPregunta_SeGuardaSinEllas()
        {
            Assert.IsFalse(await RepositorioPreparacionAlmacen.HayColumnasParada(() => throw new InvalidOperationException("sin conexión")));
        }

        [TestMethod]
        public void Columnas_SeMiranConColLength()
        {
            string sql = RepositorioPreparacionAlmacen.SQL_HAY_COLUMNAS_PARADA;
            StringAssert.Contains(sql, "COL_LENGTH('dbo.PreparacionEscaneos', 'PendienteParada')");
            StringAssert.Contains(sql, "COL_LENGTH('dbo.PreparacionEscaneos', 'TotalParada')");
        }
    }
}

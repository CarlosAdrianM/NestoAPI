using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Ubicaciones;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Ubicaciones
{
    /// <summary>
    /// NestoAPI#594 (corte 2): Registrar escribe en UbicacionesMovimientos detrás del interruptor Ubicaciones:RegistrarMovimientos.
    /// </summary>
    [TestClass]
    public class RegistroMovimientosUbicacionSqlTests
    {
        private static MovimientoUbicacion Movimiento() => new MovimientoUbicacion
        {
            Tipo = TipoMovimientoUbicacion.ReservarParaReposicion,
            Empresa = "1  ",
            Almacen = "ALG",
            Producto = "23101          ",
            NumeroOrdenUbicacion = 987654,
            HuecoOrigen = HuecoUbicacion.De("002", "002", "004"),
            HuecoDestino = HuecoUbicacion.De("002", "002", "004"),
            Cantidad = 6,
            EstadoAnterior = EstadosUbicacion.LIBRE,
            EstadoNuevo = EstadosUbicacion.RESERVA_REPOSICION,
            Documento = DocumentoUbicacion.De(TipoDocumentoUbicacion.LineaReposicion, 1234567),
            Usuario = "NUEVAVISION\\Carlos",
            Momento = new DateTime(2026, 10, 14, 9, 30, 0)
        };

        [TestMethod]
        public void InterruptorApagado_NoEscribe()
        {
            int llamadas = 0;
            var registro = new RegistroMovimientosUbicacionSql(() => false, (sql, p) => llamadas++);

            registro.Registrar(Movimiento());

            Assert.AreEqual(0, llamadas);
        }

        [TestMethod]
        public void InterruptorEncendido_EscribeLaFilaConTodosLosCampos()
        {
            string sqlLanzado = null;
            SqlParameter[] parametros = null;
            var registro = new RegistroMovimientosUbicacionSql(() => true, (sql, p) => { sqlLanzado = sql; parametros = p; });

            registro.Registrar(Movimiento());

            Assert.AreEqual(RegistroMovimientosUbicacionSql.SQL_INSERTAR, sqlLanzado);
            Dictionary<string, object> v = parametros.ToDictionary(p => p.ParameterName, p => p.Value);
            Assert.AreEqual(18, v.Count);
            Assert.AreEqual("ReservarParaReposicion", v["@p0"]);
            Assert.AreEqual("1", v["@p1"]);
            Assert.AreEqual("ALG", v["@p2"]);
            Assert.AreEqual("23101", v["@p3"]);
            Assert.AreEqual(987654, v["@p4"]);
            Assert.AreEqual("002", v["@p5"]);
            Assert.AreEqual("002", v["@p6"]);
            Assert.AreEqual("004", v["@p7"]);
            Assert.AreEqual("002", v["@p8"]);
            Assert.AreEqual("002", v["@p9"]);
            Assert.AreEqual("004", v["@p10"]);
            Assert.AreEqual(6, v["@p11"]);
            Assert.AreEqual(0, v["@p12"]);
            Assert.AreEqual(4, v["@p13"]);
            Assert.AreEqual("LineaReposicion", v["@p14"]);
            Assert.AreEqual(1234567, v["@p15"]);
            Assert.AreEqual("NUEVAVISION\\Carlos", v["@p16"]);
            Assert.AreEqual(new DateTime(2026, 10, 14, 9, 30, 0), v["@p17"]);
        }

        [TestMethod]
        public void SinHuecoNiDocumento_VanANulo()
        {
            SqlParameter[] parametros = null;
            var registro = new RegistroMovimientosUbicacionSql(() => true, (sql, p) => parametros = p);
            MovimientoUbicacion m = Movimiento();
            m.HuecoOrigen = null;
            m.HuecoDestino = HuecoUbicacion.De(null, null, null);
            m.Documento = null;
            m.NumeroOrdenUbicacion = null;
            m.EstadoAnterior = null;

            registro.Registrar(m);

            Dictionary<string, object> v = parametros.ToDictionary(p => p.ParameterName, p => p.Value);
            foreach (string nulo in new[] { "@p4", "@p5", "@p6", "@p7", "@p8", "@p9", "@p10", "@p12", "@p14", "@p15" })
            {
                Assert.AreEqual(DBNull.Value, v[nulo], nulo);
            }
        }

        [TestMethod]
        public void SiFallaAlApuntar_SeAvisaYNoSeRompeLaOperacion()
        {
            Exception avisada = null;
            var registro = new RegistroMovimientosUbicacionSql(() => true,
                (sql, p) => throw new InvalidOperationException("Se denegó el permiso INSERT"), ex => avisada = ex);

            registro.Registrar(Movimiento());

            Assert.IsNotNull(avisada);
            StringAssert.Contains(avisada.Message, "UbicacionesMovimientos");
        }

        [TestMethod]
        public void Sql_SinTablaNoHaceNada()
        {
            StringAssert.StartsWith(RegistroMovimientosUbicacionSql.SQL_INSERTAR.Trim(), "IF OBJECT_ID(N'dbo.UbicacionesMovimientos', N'U') IS NOT NULL");
        }

        [TestMethod]
        public void Interruptor_SoloConTrueExplicito()
        {
            Assert.IsTrue(RegistroMovimientosUbicacionSql.EstaActivo("true"));
            Assert.IsTrue(RegistroMovimientosUbicacionSql.EstaActivo(" True "));
            Assert.IsFalse(RegistroMovimientosUbicacionSql.EstaActivo(null));
            Assert.IsFalse(RegistroMovimientosUbicacionSql.EstaActivo(""));
            Assert.IsFalse(RegistroMovimientosUbicacionSql.EstaActivo("false"));
            Assert.IsFalse(RegistroMovimientosUbicacionSql.EstaActivo("1"));
        }

        [TestMethod]
        public void Puerta_RegistrarPasaPorElRegistro()
        {
            var apuntados = new List<MovimientoUbicacion>();
            var puerta = new PuertaUbicacionesSql(FakeItEasy.A.Fake<NestoAPI.Models.NVEntities>(), new RegistroEnLista(apuntados), () => new DateTime(2026, 10, 14));

            puerta.Registrar(new MovimientoUbicacion { Tipo = TipoMovimientoUbicacion.Falta });

            Assert.AreEqual(1, apuntados.Count);
            Assert.AreEqual(new DateTime(2026, 10, 14), apuntados[0].Momento);
        }

        private sealed class RegistroEnLista : IRegistroMovimientosUbicacion
        {
            private readonly List<MovimientoUbicacion> lista;
            public RegistroEnLista(List<MovimientoUbicacion> lista) => this.lista = lista;
            public void Registrar(MovimientoUbicacion movimiento) => lista.Add(movimiento);
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>NestoAPI#866 (corte 3): las columnas opcionales de SugerenciasContacto y el cierre a 7 días.</summary>
    [TestClass]
    public class DatosSugerenciasContactoTests
    {
        [TestMethod]
        public void SqlActualizar_UnUpdateConUnValuesPorFila()
        {
            string sql = DatosSugerenciasContactoSql.SqlActualizar(2);

            StringAssert.Contains(sql, "UPDATE s SET s.Origen = v.Origen, s.ProductosOferta = v.ProductosOferta");
            StringAssert.Contains(sql, "(VALUES (@p0, @p1, @p2), (@p3, @p4, @p5)) v (Id, Origen, ProductosOferta)");
            StringAssert.Contains(sql, "ON s.Id = v.Id");
        }

        [TestMethod]
        public void Parametros_EnOrdenConNullComoDBNullYRecortados()
        {
            var filas = new List<DatosSugerenciaRegistrada>
            {
                new DatosSugerenciaRegistrada { Id = 7, Origen = "Ofertas", ProductosOferta = "[{\"p\":\"45929\"}]" },
                new DatosSugerenciaRegistrada { Id = 8, Origen = "Una cadena de origen demasiado larga", ProductosOferta = " " }
            };

            object[] parametros = DatosSugerenciasContactoSql.Parametros(filas);

            Assert.AreEqual(6, parametros.Length);
            Assert.AreEqual(7, ((SqlParameter)parametros[0]).Value);
            Assert.AreEqual("@p0", ((SqlParameter)parametros[0]).ParameterName);
            Assert.AreEqual("Ofertas", ((SqlParameter)parametros[1]).Value);
            Assert.AreEqual("[{\"p\":\"45929\"}]", ((SqlParameter)parametros[2]).Value);
            Assert.AreEqual(8, ((SqlParameter)parametros[3]).Value);
            Assert.AreEqual(DatosSugerenciasContactoSql.LONGITUD_ORIGEN, ((string)((SqlParameter)parametros[4]).Value).Length);
            Assert.AreEqual(DBNull.Value, ((SqlParameter)parametros[5]).Value, "en blanco = NULL");
        }

        [TestMethod]
        public void SqlCerrar_PedidoComoElModeloYProductoBuscadoEnElJson()
        {
            string sql = SugerenciasContactoResultadoJobsService.SQL_CERRAR;

            StringAssert.Contains(sql, "l.TipoLinea = 1 AND l.[Base Imponible] > 0 AND cab.NotaEntrega = 0 AND l.Estado >= -1");
            StringAssert.Contains(sql, "s.ProductosOferta LIKE N'%\"p\":\"' + RTRIM(l.Producto) + N'\"%'");
            StringAssert.Contains(sql, "WHERE s.ResultadoCalculadoEl IS NULL AND s.Fecha >= @Desde AND s.Fecha < @Hasta");
            StringAssert.Contains(sql, "DATEADD(day, @DiasVentana + 1, CAST(s.Fecha AS date))");
        }

        [TestMethod]
        public void GuardarAlRegistrar_SinFilasConId_NoTocaLaBaseDeDatos()
        {
            // Con un contexto de verdad pero sin filas válidas no se abre ninguna conexión (ni se comprueban las columnas).
            var datos = new DatosSugerenciasContactoSql(new NestoAPI.Models.NVEntities());

            datos.GuardarAlRegistrar(new List<DatosSugerenciaRegistrada> { new DatosSugerenciaRegistrada { Id = 0, Origen = "Probabilidad" } });
            datos.GuardarAlRegistrar(null);
        }
    }
}

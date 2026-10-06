using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Ubicaciones
{
    /// <summary>
    /// NestoAPI#594 (corte 1): la puerta única de Ubicaciones. Las sentencias se comprueban sobre sus constantes (el mismo
    /// resultado que prdUbicarReposicion, el «terminar» de Nesto viejo y prdDeshacerUbicacionReposicion); el cableado con
    /// la reposición, con una puerta falsa.
    /// </summary>
    [TestClass]
    public class PuertaUbicacionesTests
    {
        // ---------------------------------------------------------------- SQL

        [TestMethod]
        public void Sql_LibresParaReservar_Estado0Y2DeLaEmpresaOSuEspejoBloqueados()
        {
            string sql = PuertaUbicacionesSql.SQL_LIBRES_PARA_RESERVA;
            StringAssert.Contains(sql, "(Empresa = @p0 OR Empresa = @p1)");
            StringAssert.Contains(sql, "Estado IN (0, 2)");
            StringAssert.Contains(sql, "WITH (UPDLOCK, HOLDLOCK)");
            StringAssert.Contains(sql, "FechaCreación AS FechaCreacion");
        }

        [TestMethod]
        public void Sql_LaReservaApuntaALaLineaDeEntradaConEstado4()
        {
            // Entera: la propia fila pasa a 4 con NºOrdenRepo = la línea de ENTRADA
            StringAssert.Contains(PuertaUbicacionesSql.SQL_RESERVAR_FILA_ENTERA, "SET Estado = 4, [NºOrdenRepo] = @p1, Usuario = @p2");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_RESERVAR_FILA_ENTERA, "Cantidad = @p3", "Solo si sigue teniendo lo que se leyó");
            // Una parte: se resta y se inserta la reserva en el mismo hueco
            StringAssert.Contains(PuertaUbicacionesSql.SQL_RESTAR_DE_LIBRE, "Cantidad = Cantidad - @p1");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_RESTAR_DE_LIBRE, "Cantidad > @p1");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_INSERTAR_RESERVA, "Pasillo, Fila, Columna, 4, @p2, @p3 FROM Ubicaciones WHERE [NºOrden] = @p0");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_INSERTAR_RESERVA, "SCOPE_IDENTITY()");
            Assert.IsFalse(PuertaUbicacionesSql.SQL_INSERTAR_RESERVA.Contains("OUTPUT"), "Ubicaciones tiene triggers: OUTPUT sin INTO falla");
        }

        [TestMethod]
        public void Sql_SalidaDeReposicion_De4AMenos4ConCantidadNegativaYNumeroDeTraspaso()
        {
            string sql = PuertaUbicacionesSql.SQL_SALIDA_DE_REPOSICION;
            StringAssert.Contains(sql, "SET Cantidad = -Cantidad, Estado = -4, [NºTraspasoRepo] = @p0");
            StringAssert.Contains(sql, "WHERE Estado = 4 AND [NºOrdenRepo] IN ({0})");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_RESERVAS_DE_LAS_LINEAS, "WHERE Estado = 4 AND [NºOrdenRepo] IN ({0})");
        }

        [TestMethod]
        public void Sql_LiberarYAnular_VuelvenAlHuecoComoPrdDeshacerUbicacionReposicion()
        {
            StringAssert.Contains(PuertaUbicacionesSql.SQL_RESERVAS_DE_LA_LINEA, "WHERE [NºOrdenRepo] = @p0 AND Estado = 4");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_SALIDAS_DEL_TRASPASO, "WHERE [NºTraspasoRepo] = @p0 AND Estado = -4 AND [Almacén] = @p1");
            string sumar = PuertaUbicacionesSql.SQL_SUMAR_A_LIBRE_DEL_HUECO;
            StringAssert.Contains(sumar, "Pasillo = @p5 AND Fila = @p6 AND Columna = @p7");
            StringAssert.Contains(sumar, "Estado = 0 AND PedidoVta IS NULL AND [AlbaránVta] IS NULL");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_DEVOLVER_FILA_A_LIBRE, "Estado = CASE WHEN Pasillo IS NULL THEN 2 ELSE 0 END");
            StringAssert.Contains(PuertaUbicacionesSql.SQL_DEVOLVER_FILA_A_LIBRE, "[NºOrdenRepo] = NULL, [NºTraspasoRepo] = NULL");
        }

        [TestMethod]
        public void Sql_TodasLasEscriturasPonenElUsuarioDeAuditoria()
        {
            foreach (string sql in new[]
            {
                PuertaUbicacionesSql.SQL_RESERVAR_FILA_ENTERA, PuertaUbicacionesSql.SQL_RESTAR_DE_LIBRE, PuertaUbicacionesSql.SQL_INSERTAR_RESERVA,
                PuertaUbicacionesSql.SQL_SALIDA_DE_REPOSICION, PuertaUbicacionesSql.SQL_SUMAR_A_LIBRE_DEL_HUECO, PuertaUbicacionesSql.SQL_DEVOLVER_FILA_A_LIBRE
            })
            {
                StringAssert.Contains(sql, "Usuario");
            }
        }

        // ---------------------------------------------------------------- Operaciones aún fuera de la puerta

        [TestMethod]
        public async Task LasOperacionesSinMigrar_DicenQuienLasHaceHoy()
        {
            using (var db = new NVEntities())
            {
                var puerta = new PuertaUbicacionesSql(db);
                NotImplementedException picking = await Assert.ThrowsExceptionAsync<NotImplementedException>(() => puerta.ReservarParaPicking("1", "ALG", 1, "x", 1, "u"));
                StringAssert.Contains(picking.Message, "prdUbicacionPicking");
                StringAssert.Contains(picking.Message, "#594");
                NotImplementedException inventario = await Assert.ThrowsExceptionAsync<NotImplementedException>(() => puerta.RegularizarPorInventario("1", "ALG", 1, "u"));
                StringAssert.Contains(inventario.Message, "prdContabilizarInventario");
                NotImplementedException albaran = await Assert.ThrowsExceptionAsync<NotImplementedException>(() => puerta.SalidaDePicking("1", 1, null, "u"));
                StringAssert.Contains(albaran.Message, "prdCrearAlbaránVta");
            }
        }

        [TestMethod]
        public void Registrar_PasaPorElPuntoUnicoConElMomento()
        {
            IRegistroMovimientosUbicacion registro = A.Fake<IRegistroMovimientosUbicacion>();
            var momento = new DateTime(2026, 10, 6, 16, 0, 0);
            using (var db = new NVEntities())
            {
                var puerta = new PuertaUbicacionesSql(db, registro, () => momento);
                var movimiento = new MovimientoUbicacion { Tipo = TipoMovimientoUbicacion.SalidaDeReposicion, Producto = "12291", Cantidad = 4 };

                puerta.Registrar(movimiento);

                A.CallTo(() => registro.Registrar(A<MovimientoUbicacion>.That.Matches(m => m.Momento == momento && m.Producto == "12291"))).MustHaveHappenedOnceExactly();
            }
        }
    }
}

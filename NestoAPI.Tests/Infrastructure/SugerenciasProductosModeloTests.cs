using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Features;
using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.SugerenciasProductos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>NestoAPI#669: dos «🚫 Quitar» de una familia la excluyen para ese cliente.</summary>
    public partial class SugerenciasProductosTests
    {
        private SugerenciaMostrada Mostrada(Guid sesion, string producto) => _mostradas.Single(m => m.SesionId == sesion && m.Producto == producto);

        // ---------------- NestoAPI#669: «Quitar» aprende la familia ----------------

        private void Quitado(string producto, string familia, string cliente = CLIENTE, int hace = 10)
            => _rechazosConFamilia.Add(Tuple.Create(new RechazoCliente { Producto = producto, Familia = familia, Quitado = true }, cliente, HOY.Date.AddDays(-hace)));

        [TestMethod]
        public void FamiliasQuitadas_DosQuitadosDistintosDeLaFamilia_NoLeInteresaNoCuenta()
        {
            var uno = new List<RechazoCliente> { new RechazoCliente { Producto = "A1", Familia = "ANU", Quitado = true } };
            Assert.AreEqual(0, MotorSugerenciasProductos.FamiliasQuitadas(uno, null, null).Count, "Uno solo no excluye la familia");

            var dos = new List<RechazoCliente>(uno) { new RechazoCliente { Producto = "A2", Familia = "ANU", Quitado = true } };
            CollectionAssert.AreEqual(new[] { "ANU" }, MotorSugerenciasProductos.FamiliasQuitadas(dos, null, null).ToArray());

            var conRechazo = new List<RechazoCliente>(uno) { new RechazoCliente { Producto = "A2", Familia = "ANU", Quitado = false } };
            Assert.AreEqual(0, MotorSugerenciasProductos.FamiliasQuitadas(conRechazo, null, null).Count, "«No le interesa» no cuenta");

            // El de la sesión (aún sin leer de la BD) cuenta, con la familia de su ficha
            var fichas = new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase)
            {
                { "A3", new FichaProductoSugerencia { Producto = "A3", Familia = "ANU" } }
            };
            var sesion = new[] { new SugerenciaMostrada { Producto = "A3", Respuesta = RespuestasSugerencia.DESCARTADO_VENDEDOR } };
            CollectionAssert.AreEqual(new[] { "ANU" }, MotorSugerenciasProductos.FamiliasQuitadas(uno, sesion, fichas).ToArray());
            Assert.AreEqual(2, MotorSugerenciasProductos.MINIMO_QUITADOS_FAMILIA);
        }

        [TestMethod]
        public async Task FamiliaQuitada_UnQuitadoNoExcluyeLaFamilia()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU");

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            Assert.IsTrue(r.Sugerencias.Any(s => s.Producto == "POT1"));
        }

        [TestMethod]
        public async Task FamiliaQuitada_DosQuitadosExcluyenTodaLaFamilia()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU");
            Quitado("OTRO2", "ANU", hace: 50);

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            Assert.IsFalse(r.Sugerencias.Any(s => s.Producto == "POT1"));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_FAMILIA_QUITADA, MotorSugerenciasProductos.MotivoDescarte("POT1", _fichas["POT1"],
                FiltroGrupoVendedor.Todos, null, null, null, new HashSet<string> { "ANU" }));
        }

        [TestMethod]
        public async Task FamiliaQuitada_OtroClienteNoSeVeAfectado()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU", cliente: "2002");
            Quitado("OTRO2", "ANU", cliente: "2002");

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            Assert.IsTrue(r.Sugerencias.Any(s => s.Producto == "POT1"));
        }

        [TestMethod]
        public async Task FamiliaQuitada_FueraDeLaVentanaDejaDeExcluir()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU");
            Quitado("OTRO2", "ANU", hace: MotorSugerenciasProductos.DIAS_SIN_REPETIR_RECHAZO + 1);

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            Assert.IsTrue(r.Sugerencias.Any(s => s.Producto == "POT1"));
            StringAssert.Contains(SugerenciasProductosSql.SQL_RECHAZADOS, "m.FechaRespuesta >= @p2");
            StringAssert.Contains(SugerenciasProductosSql.SQL_RECHAZADOS, "p.Familia");
        }

        [TestMethod]
        public async Task FamiliaQuitada_DosQuitarEnLaMismaSesion_LaVisibleDeEsaFamiliaSeVa()
        {
            foreach (string p in new[] { "LETOCA1", "POT1", "LETOCA2" })
            {
                _fichas[p].Familia = "ANU";
            }
            Guid sesion = Guid.NewGuid();
            _ = await _servicio.Calcular(Peticion(sesion), "carlos");
            _ = await _servicio.Responder(Mostrada(sesion, "LETOCA1").Id, new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.DESCARTADO_VENDEDOR });
            ResultadoRespuestaSugerenciaDTO r = await _servicio.Responder(Mostrada(sesion, "POT1").Id,
                new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.DESCARTADO_VENDEDOR });

            Assert.IsFalse(r.Sugerencias.Any(s => s.Producto == "LETOCA2"), "Dos «Quitar» de ANU: tampoco LETOCA2");
        }
    }
}

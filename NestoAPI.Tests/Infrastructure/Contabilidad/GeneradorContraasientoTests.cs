using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    /// <summary>
    /// NestoAPI#397: el contraasiento anula al original SIN borrar nada. Lo que de verdad hay que
    /// cubrir es el mapeo campo a campo: si el contraasiento no cuadra con el original en cada
    /// dimensión (delegación, vendedor, ruta, forma de venta...), deja de anularlo en cuanto un
    /// informe agrupe por cualquiera de ellas.
    /// </summary>
    [TestClass]
    public class GeneradorContraasientoTests
    {
        private const string DIARIO = "_contraas";
        private const string USUARIO = "NUEVAVISION\\Carlos";
        private static readonly DateTime FECHA_ORIGINAL = new DateTime(2026, 8, 20);

        private static NestoAPI.Models.Contabilidad ApunteCompleto() => new NestoAPI.Models.Contabilidad
        {
            Empresa = "1  ",
            TipoApunte = "1",
            Nº_Cuenta = "43000001",
            Nº_Documento = "NV26/0001",
            Concepto = "Cobro de la factura NV2612345",
            Debe = 121.55M,
            Haber = 0M,
            Fecha = FECHA_ORIGINAL,
            Asiento = 161588,
            Delegación = "ALG",
            FormaVenta = "WEB",
            Diario = "_bancos",
            Origen = "BAN",
            CentroCoste = "CC1",
            Departamento = "DEP",
            // Se rellenan tambien los de auditoria: si se quedan a default, el test por reflexion
            // los ve iguales en original y contraasiento y no caza que falten por mapear.
            Usuario = "NUEVAVISION\\Alfredo",
            Fecha_Modificación = new DateTime(2026, 8, 20, 10, 30, 0)
        };

        [TestMethod]
        public void GenerarLinea_IntercambiaDebeYHaber()
        {
            NestoAPI.Models.Contabilidad original = ApunteCompleto();

            PreContabilidad contra = GeneradorContraasiento.GenerarLinea(original, DIARIO, USUARIO, FECHA_ORIGINAL);

            Assert.AreEqual(original.Haber, contra.Debe);
            Assert.AreEqual(original.Debe, contra.Haber);
            Assert.AreEqual(0M, original.Debe + original.Haber - (contra.Debe + contra.Haber),
                "El original y su contraasiento tienen que sumar cero");
        }

        [TestMethod]
        public void GenerarLinea_CopiaTalCualTodasLasDemasDimensiones()
        {
            NestoAPI.Models.Contabilidad original = ApunteCompleto();

            PreContabilidad contra = GeneradorContraasiento.GenerarLinea(original, DIARIO, USUARIO, FECHA_ORIGINAL);

            // Con el padding incluido: si se recortara, dejaría de casar con el original.
            Assert.AreEqual("1  ", contra.Empresa);
            Assert.AreEqual("1", contra.TipoApunte);
            Assert.AreEqual("43000001", contra.Nº_Cuenta);
            Assert.AreEqual("NV26/0001", contra.Nº_Documento);
            Assert.AreEqual("ALG", contra.Delegación);
            Assert.AreEqual("WEB", contra.FormaVenta);
            Assert.AreEqual("CC1", contra.CentroCoste);
            Assert.AreEqual("DEP", contra.Departamento);
            Assert.AreEqual("BAN", contra.Origen);
        }

        /// <summary>
        /// Guarda contra el olvido: si alguien añade una columna a Contabilidad que también existe
        /// en PreContabilidad y no la mapea, este test lo caza. Es el fallo más probable de esta
        /// funcionalidad, y el más silencioso: el contraasiento se contabiliza igual y solo se nota
        /// cuando un informe agrupa por la dimensión que falta.
        /// </summary>
        [TestMethod]
        public void GenerarLinea_NoSeOlvidaNingunCampoQueCompartanLasDosTablas()
        {
            // Debe/Haber y Concepto cambian a propósito. Fecha, Diario y Usuario los pone quien
            // contabiliza. Asiento lo asigna el SP. Liquidado se vacía a propósito (ver su test).
            var cambianAProposito = new HashSet<string>
            {
                "Debe", "Haber", "Concepto", "Fecha", "Diario", "Asiento", "Liquidado",
                // Auditoria: son del que hace el contraasiento, no del apunte original.
                "Usuario", "Fecha_Modificación"
            };

            NestoAPI.Models.Contabilidad original = ApunteCompleto();
            PreContabilidad contra = GeneradorContraasiento.GenerarLinea(original, DIARIO, USUARIO, FECHA_ORIGINAL);

            IEnumerable<PropertyInfo> compartidas = typeof(NestoAPI.Models.Contabilidad).GetProperties()
                .Where(p => p.PropertyType.IsValueType || p.PropertyType == typeof(string))
                .Where(p => !cambianAProposito.Contains(p.Name))
                .Where(p => typeof(PreContabilidad).GetProperty(p.Name) != null);

            var sinMapear = new List<string>();
            foreach (PropertyInfo propiedad in compartidas)
            {
                object valorOriginal = propiedad.GetValue(original);
                object valorContra = typeof(PreContabilidad).GetProperty(propiedad.Name).GetValue(contra);

                // Solo se comprueban las que el apunte de ejemplo trae rellenas.
                if (valorOriginal == null || Equals(valorOriginal, string.Empty))
                {
                    continue;
                }
                if (!Equals(valorOriginal, valorContra))
                {
                    sinMapear.Add($"{propiedad.Name}: original='{valorOriginal}' contraasiento='{valorContra}'");
                }
            }

            Assert.AreEqual(0, sinMapear.Count,
                "Campos que comparten las dos tablas y NO se copian al contraasiento:\n" + string.Join("\n", sinMapear));
        }

        [TestMethod]
        public void GenerarLinea_NuncaLiquidaEfectos()
        {
            // Decisión de Carlos (24/08/26): copiar el Liquidado del original haría que
            // prdContabilizar llamase a prdLiquidar OTRA VEZ sobre el mismo efecto (#296/#311).
            // El contraasiento revierte el importe y nada más.
            NestoAPI.Models.Contabilidad original = ApunteCompleto();

            PreContabilidad contra = GeneradorContraasiento.GenerarLinea(original, DIARIO, USUARIO, FECHA_ORIGINAL);

            Assert.IsNull(contra.Liquidado);
        }

        [TestMethod]
        public void GenerarLinea_LaAuditoriaEsDeQuienHaceElContraasiento_NoDelApunteOriginal()
        {
            // El original conserva su Usuario y su Fecha_Modificacion; el contraasiento es un
            // apunte NUEVO y responde de quien lo crea y de cuando.
            NestoAPI.Models.Contabilidad original = ApunteCompleto();
            DateTime antes = DateTime.Now.AddSeconds(-5);

            PreContabilidad contra = GeneradorContraasiento.GenerarLinea(original, DIARIO, USUARIO, FECHA_ORIGINAL);

            Assert.AreEqual(USUARIO, contra.Usuario);
            Assert.AreNotEqual(original.Usuario, contra.Usuario, "No es el usuario que hizo el apunte original");
            Assert.IsTrue(contra.Fecha_Modificación >= antes, "La fecha de modificacion es la de AHORA");
            Assert.AreNotEqual(original.Fecha_Modificación, contra.Fecha_Modificación);
            Assert.AreEqual(FECHA_ORIGINAL, contra.Fecha,
                "La fecha CONTABLE si es la del original: es otra cosa distinta de la de auditoria");
        }

        [TestMethod]
        public void ConceptoContraasiento_AnteponeElPrefijo()
        {
            Assert.AreEqual("Contraasiento Cobro de la factura NV2612345",
                GeneradorContraasiento.ConceptoContraasiento("Cobro de la factura NV2612345"));
        }

        [TestMethod]
        public void ConceptoContraasiento_SiNoCabe_RecortaPorElFinalYNuncaPierdeElPrefijo()
        {
            string largo = new string('X', 80);

            string resultado = GeneradorContraasiento.ConceptoContraasiento(largo);

            Assert.AreEqual(50, resultado.Length, "No puede pasarse del ancho de la columna");
            Assert.IsTrue(resultado.StartsWith("Contraasiento "),
                "El prefijo es lo que hace entendible el apunte de un vistazo: es lo último que se pierde");
        }

        [TestMethod]
        public void ConceptoContraasiento_ConceptoVacio_NoRevienta()
        {
            Assert.AreEqual("Contraasiento", GeneradorContraasiento.ConceptoContraasiento(null).Trim());
            Assert.AreEqual("Contraasiento", GeneradorContraasiento.ConceptoContraasiento("   ").Trim());
        }

        [TestMethod]
        public void AgruparPorAsiento_ApuntesDeAsientosDistintos_VanCadaUnoPorSuLado()
        {
            // Decisión de Carlos: un contraasiento por cada asiento de origen. Mezclarlos juntaría
            // reversiones de operaciones que no tenían nada que ver.
            var a1 = ApunteCompleto(); a1.Asiento = 100;
            var a2 = ApunteCompleto(); a2.Asiento = 100;
            var b1 = ApunteCompleto(); b1.Asiento = 200;

            List<IGrouping<int?, NestoAPI.Models.Contabilidad>> grupos =
                GeneradorContraasiento.AgruparPorAsiento(new[] { a1, b1, a2 }).ToList();

            Assert.AreEqual(2, grupos.Count);
            Assert.AreEqual(100, grupos[0].Key);
            Assert.AreEqual(2, grupos[0].Count());
            Assert.AreEqual(200, grupos[1].Key);
            Assert.AreEqual(1, grupos[1].Count());
        }

        [TestMethod]
        public void Generar_DevuelveUnaLineaPorApunte()
        {
            List<PreContabilidad> lineas = GeneradorContraasiento.Generar(
                new[] { ApunteCompleto(), ApunteCompleto() }, DIARIO, USUARIO, FECHA_ORIGINAL);

            Assert.AreEqual(2, lineas.Count);
            Assert.IsTrue(lineas.All(l => l.Diario == DIARIO && l.Usuario == USUARIO));
        }

        [TestMethod]
        public void Generar_UsaLaFechaQueSeLePasa_NoLaDelApunte()
        {
            // La fecha se decide fuera: la del original o, si su mes esta cerrado, la que elija el
            // usuario. El generador no la deduce.
            var hoy = new DateTime(2026, 8, 24);

            List<PreContabilidad> lineas = GeneradorContraasiento.Generar(
                new[] { ApunteCompleto() }, DIARIO, USUARIO, hoy);

            Assert.AreEqual(hoy, lineas.Single().Fecha);
        }
    }
}

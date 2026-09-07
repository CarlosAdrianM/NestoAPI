using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Seguridad;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Web.Http.Controllers;

namespace NestoAPI.Tests.Infrastructure.Seguridad
{
    /// <summary>
    /// NestoAPI#459: los GET de PlazosPago y FormasPago devolvían la InfoDeuda (deuda vencida,
    /// impagados, motivo de restricción) de cualquier código de cliente que se pasara en la URL,
    /// sin credencial ninguna. Ahora exigen JWT o API key, y un cliente final solo puede preguntar
    /// por sí mismo.
    /// </summary>
    [TestClass]
    public class CredencialPlazosYFormasPagoTests
    {
        private static void AssertExigeCredencial(System.Type controlador, string metodo, int parametros)
        {
            MethodInfo info = controlador.GetMethods()
                .FirstOrDefault(m => m.Name == metodo && m.GetParameters().Length == parametros);
            Assert.IsNotNull(info, $"No existe {controlador.Name}.{metodo} con {parametros} parámetros");

            AutorizadoOApiKeyAttribute atributo = info
                .GetCustomAttributes<AutorizadoOApiKeyAttribute>(inherit: false).FirstOrDefault();

            Assert.IsNotNull(atributo,
                $"{metodo}({parametros} parámetros) tiene que exigir credencial: sin el atributo devuelve " +
                "la deuda de cualquier cliente a quien pase por ahí, y quedarse abierto no se nota.");
            Assert.AreEqual("ApiKeyPrestashop", atributo.NombreSetting);
            Assert.AreEqual("X-API-KEY", atributo.Cabecera);
        }

        [TestMethod]
        public void PlazosPagoPorCliente_ExigeCredencial()
        {
            AssertExigeCredencial(typeof(PlazosPagoController), "GetPlazosPago", 2);
        }

        [TestMethod]
        public void PlazosPagoPorClienteYFormaPago_ExigeCredencial()
        {
            AssertExigeCredencial(typeof(PlazosPagoController), "GetPlazosPago", 4);
        }

        [TestMethod]
        public void ConInfoDeuda_ExigeCredencial()
        {
            AssertExigeCredencial(typeof(PlazosPagoController), "GetPlazosPagoConInfoDeuda", 2);
        }

        [TestMethod]
        public void CondicionesPago_ExigeCredencial()
        {
            AssertExigeCredencial(typeof(PlazosPagoController), "GetCondicionesPago", 3);
        }

        [TestMethod]
        public void FormasPagoPorCliente_ExigeCredencial()
        {
            AssertExigeCredencial(typeof(FormasPagoController), "GetFormasPago", 2);
        }

        [TestMethod]
        public void FormasPagoPorClienteYTotal_ExigeCredencial()
        {
            AssertExigeCredencial(typeof(FormasPagoController), "GetFormasPago", 4);
        }

        [TestMethod]
        public void ElInterruptorNaceApagado_ParaNoTumbarALosNestoQueNoHanReiniciado()
        {
            // Nesto se distribuye por ClickOnce: cada puesto actualiza cuando reinicia la
            // aplicación, no cuando publicamos. Si la API exigiera credencial el mismo día del
            // despliegue, el SelectorPlazosPago viejo (sin JWT) se quedaría sin lista de plazos y
            // sin dar ningún error. Mientras el interruptor esté apagado se deja pasar y se apunta
            // en ELMAH quién sigue llamando sin credencial.
            Assert.IsFalse(AutorizadoOApiKeyAttribute.Exigir,
                "El Web.config publicado tiene que traerlo en false: se enciende cuando el aviso " +
                "de ELMAH deje de aparecer, no antes");
            Assert.AreEqual("Seguridad:ExigirCredencialPlazosYFormasPago",
                AutorizadoOApiKeyAttribute.CLAVE_EXIGIR);
        }

        // ===== Quién puede preguntar por qué cliente =====

        private static ClaimsIdentity IdentidadCliente(string cliente)
        {
            // Lo que emite api/auth/token para TiendasNuevaVision.
            return new ClaimsIdentity(new[] { new Claim("cliente", cliente) }, "JWT");
        }

        [TestMethod]
        public void UnClienteFinal_PuedePreguntarPorSuPropioCliente()
        {
            Assert.IsTrue(AutorizadoOApiKeyAttribute.PuedeConsultarCliente(IdentidadCliente("15191"), "15191"));
        }

        [TestMethod]
        public void UnClienteFinal_NoPuedePreguntarPorOtroCliente()
        {
            // El agujero de la issue: con solo [Authorize] esto seguiría colando y le enseñaría a
            // un cliente si otro tiene impagados.
            Assert.IsFalse(AutorizadoOApiKeyAttribute.PuedeConsultarCliente(IdentidadCliente("15191"), "31517"));
        }

        [TestMethod]
        public void UnClienteFinal_ConElRellenoDelCharDeLaBD_SigueSiendoElMismo()
        {
            // El código de cliente es char en la base de datos y viaja con relleno según quién lo
            // mande; comparar en crudo dejaría fuera a un cliente de los suyos propios.
            Assert.IsTrue(AutorizadoOApiKeyAttribute.PuedeConsultarCliente(IdentidadCliente("15191"), " 15191 "));
        }

        [TestMethod]
        public void UnEmpleado_PuedePreguntarPorCualquierCliente()
        {
            ClaimsIdentity empleado = new ClaimsIdentity(
                new[] { new Claim("IsEmployee", "true") }, "JWT");

            Assert.IsTrue(AutorizadoOApiKeyAttribute.PuedeConsultarCliente(empleado, "31517"));
        }

        [TestMethod]
        public void UnVendedor_PuedePreguntarPorCualquierCliente()
        {
            // IMPORTANTE: NestoApp llama a estos endpoints con JWT de vendedor para el cliente que
            // esté visitando. ValidadorAccesoCliente deniega a los vendedores, así que usarlo aquí
            // habría dejado a los comerciales sin plazos ni formas de pago en la calle.
            ClaimsIdentity vendedor = new ClaimsIdentity(
                new[] { new Claim("IsVendedor", "true"), new Claim("Vendedor", "NV") }, "JWT");

            Assert.IsTrue(AutorizadoOApiKeyAttribute.PuedeConsultarCliente(vendedor, "31517"));
        }

        [TestMethod]
        public void ConApiKeyYSinIdentidad_NoHayClienteAlQueAtarse()
        {
            // Es la llamada de servidor a servidor de nestopago: no hay JWT del que sacar cliente.
            Assert.IsTrue(AutorizadoOApiKeyAttribute.PuedeConsultarCliente(null, "31517"));
        }

        [TestMethod]
        public void SinClienteEnLaConsulta_NoSePideNadaDeNadie()
        {
            Assert.IsTrue(AutorizadoOApiKeyAttribute.PuedeConsultarCliente(IdentidadCliente("15191"), null));
        }

        // ===== El cliente se lee de la query, no del model binding =====

        private static HttpActionContext ContextoPara(string url)
        {
            return new HttpActionContext
            {
                ControllerContext = new HttpControllerContext
                {
                    Request = new HttpRequestMessage(HttpMethod.Get, url)
                }
            };
        }

        [TestMethod]
        public void LeerClienteDeLaConsulta_LoSacaDeLaQuery()
        {
            HttpActionContext contexto = ContextoPara(
                "https://api.nuevavision.es/api/PlazosPago/ConInfoDeuda?empresa=1&cliente=15191");

            Assert.AreEqual("15191", AutorizadoOApiKeyAttribute.LeerClienteDeLaConsulta(contexto));
        }

        [TestMethod]
        public void LeerClienteDeLaConsulta_NoDistingueMayusculasEnElNombreDelParametro()
        {
            HttpActionContext contexto = ContextoPara(
                "https://api.nuevavision.es/api/FormasPago?empresa=1&Cliente=15191");

            Assert.AreEqual("15191", AutorizadoOApiKeyAttribute.LeerClienteDeLaConsulta(contexto));
        }

        [TestMethod]
        public void LeerClienteDeLaConsulta_SiNoViene_EsNulo()
        {
            HttpActionContext contexto = ContextoPara("https://api.nuevavision.es/api/PlazosPago?empresa=1");

            Assert.IsNull(AutorizadoOApiKeyAttribute.LeerClienteDeLaConsulta(contexto));
        }
    }
}

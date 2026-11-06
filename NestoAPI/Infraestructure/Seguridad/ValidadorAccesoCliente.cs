using System;
using System.Security.Claims;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// Valida si un usuario tiene acceso a recursos de un cliente específico.
    /// </summary>
    public static class ValidadorAccesoCliente
    {
        /// <summary>
        /// Resultado de la validación de acceso.
        /// </summary>
        public class ResultadoValidacion
        {
            public bool Autorizado { get; set; }
            public string Motivo { get; set; }

            public static ResultadoValidacion Permitido(string motivo = null) =>
                new ResultadoValidacion { Autorizado = true, Motivo = motivo };

            public static ResultadoValidacion Denegado(string motivo) =>
                new ResultadoValidacion { Autorizado = false, Motivo = motivo };
        }

        /// <summary>
        /// Valida si el usuario puede acceder a recursos del cliente especificado.
        ///
        /// Reglas de acceso:
        /// 1. Empleados (IsEmployee = "true"): acceso total
        /// 2. Clientes (claim "cliente"): solo a sus propios recursos
        /// 3. Vendedores (IsVendedor = "true"): con esta sobrecarga (sin comprobador), denegado. Para dejarles sus
        ///    clientes, la sobrecarga con <c>clienteDelVendedor</c>.
        /// 4. Otros: denegado
        /// </summary>
        /// <param name="identity">Identidad del usuario autenticado</param>
        /// <param name="clienteSolicitado">Código del cliente al que se quiere acceder</param>
        /// <returns>Resultado indicando si está autorizado y el motivo</returns>
        public static ResultadoValidacion ValidarAcceso(ClaimsIdentity identity, string clienteSolicitado)
            => ValidarAcceso(identity, clienteSolicitado, null);

        /// <summary>
        /// Como <see cref="ValidarAcceso(ClaimsIdentity, string)"/>, pero un vendedor de NestoApp (IsVendedor = "true" y claim
        /// "Vendedor" con su código) accede a un cliente si <paramref name="clienteDelVendedor"/>(vendedor, cliente) es true (la
        /// regla está en <see cref="ClientesDelVendedor"/>). Sin comprobador (null), el vendedor queda denegado como siempre.
        /// </summary>
        public static ResultadoValidacion ValidarAcceso(ClaimsIdentity identity, string clienteSolicitado,
            Func<string, string, bool> clienteDelVendedor)
        {
            if (identity == null)
            {
                return ResultadoValidacion.Denegado("Usuario no autenticado");
            }

            if (string.IsNullOrWhiteSpace(clienteSolicitado))
            {
                return ResultadoValidacion.Denegado("Cliente no especificado");
            }

            // 1. Empleados (Nesto WPF via Windows Auth): acceso total
            var isEmployee = identity.FindFirst("IsEmployee")?.Value;
            if (isEmployee == "true")
            {
                return ResultadoValidacion.Permitido("Empleado autorizado");
            }

            // 2. Clientes (TiendasNuevaVision): solo sus propios recursos
            var clienteClaim = identity.FindFirst("cliente")?.Value;
            if (!string.IsNullOrEmpty(clienteClaim))
            {
                if (clienteClaim.Trim() == clienteSolicitado.Trim())
                {
                    return ResultadoValidacion.Permitido("Cliente accediendo a sus propios recursos");
                }
                return ResultadoValidacion.Denegado("Cliente no puede acceder a recursos de otro cliente");
            }

            // 3. Vendedores (NestoApp): solo sus clientes, si quien llama sabe comprobarlo
            var isVendedor = identity.FindFirst("IsVendedor")?.Value;
            if (isVendedor == "true")
            {
                var vendedor = identity.FindFirst("Vendedor")?.Value?.Trim();
                if (clienteDelVendedor == null)
                {
                    return ResultadoValidacion.Denegado(
                        $"Vendedor {vendedor}: acceso a clientes no implementado. " +
                        "FUTURO: verificar que el cliente esté asignado al vendedor");
                }
                if (string.IsNullOrEmpty(vendedor))
                {
                    return ResultadoValidacion.Denegado("Vendedor sin código de vendedor en el token");
                }
                return clienteDelVendedor(vendedor, clienteSolicitado.Trim())
                    ? ResultadoValidacion.Permitido("Vendedor accediendo a un cliente suyo")
                    : ResultadoValidacion.Denegado($"Vendedor {vendedor}: el cliente {clienteSolicitado.Trim()} no es suyo");
            }

            // 4. Cualquier otro caso: denegado
            return ResultadoValidacion.Denegado("Usuario sin permisos suficientes");
        }
    }
}

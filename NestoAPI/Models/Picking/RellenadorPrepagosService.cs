using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class RellenadorPrepagosService : IRellenadorPrepagosService
    {
        NVEntities db = new NVEntities();

        public string CorreoUsuario(string usuario)
        {
            if (usuario == null)
            {
                return String.Empty;
            }
            string usuarioSinDominio = usuario.Contains("\\") ? usuario.Substring(usuario.IndexOf("\\") + 1).Trim() : usuario;
            return db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == usuarioSinDominio && p.Clave == "CorreoDefecto")?.Valor;
        }

        public List<ExtractoClienteDTO> ExtractosPendientes(int pedido)
        {
            var cliente = db.CabPedidoVtas.First(p => p.Número == pedido).Nº_Cliente;
            return db.ExtractosCliente.Where(e => e.ImportePdte != 0 && e.Número == cliente)
                .Select(e => new ExtractoClienteDTO { 
                    importePendiente = e.ImportePdte,
                    estado = e.Estado
                })
                .ToList();
        }

        public List<PrepagoDTO> Prepagos(int pedido)
        {
            return db.Prepagos.Where(p => p.Pedido == pedido && p.Factura == null)
                .Select(p => new PrepagoDTO
                {
                    Importe = p.Importe
                })
                .ToList();
        }
    }
}
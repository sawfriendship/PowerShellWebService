// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

function put_to_body() {
    var result = {};
    $('#Params').children().each(function(){
        var dict = {}; $(this).find('.param_prop').each(function(i,Prop){dict[Prop.name]=Prop.value})
        if (dict['Property']) {
            var Type = dict['Type']
            var Value = dict['Value'];
            if (Type == 'String') {
                v = Value
            } else if (Type == 'Number') {
                try {v = Number(Value)} catch (e) {v = Value}
            } else if (Type == 'Bool') {
                if (!Value) {v = false} else {try {v = Boolean(JSON.parse(Value.toLowerCase()))} catch (e) {v = Value}}
            } else if (Type == 'Object') {
                if (!Value) {v = {}} else {try {v = JSON.parse(Value)} catch (e) {v = Value}}
            }

            result[dict['Property']] = v;
        }
    })
    var j_string = JSON.stringify(result);
    $('#_body').val(j_string)
}

function add_param() {
    $('#Param').clone().appendTo('#Params').show(200);
    $('._btn_del_param').bind('click', del_param)
} 

function del_param() {
    $(this).parent().hide(200, function(){ $(this).remove(); put_to_body();});
    
}

function update_url() {
    var wrapper = $('#_wrapper').val()
    var script = $('#_script').val()
    var url = `/PowerShell/${wrapper}/${script}`
    $('a#pwsh_url').attr('href',url)
    $('a#pwsh_url').html(url)
    console.log(url)
}

function send_body() {
    var method = $(this).attr('method')
    var depth = $('._depth').val()
    var outputtype = $('._outputtype').val()
    var wrapper = $('#_wrapper').val()
    var script = $('#_script').val()
    url = `/PowerShell/${wrapper}/${script}`
    var request = {
        url: url,
        method: method,
        headers: {Depth:depth},
        success: function(responce){
            console.log(url,method,responce)
            if (outputtype == 'Raw') {var result = responce
            } else if (outputtype == 'Streams') {var result = responce.Streams
            } else if (outputtype == 'PSObjects') {var result = responce.Streams.PSObjects
            } else if (outputtype == 'Error') {var result = responce.Streams.Error
            } else if (outputtype == 'Warning') {var result = responce.Streams.Warning
            } else if (outputtype == 'Verbose') {var result = responce.Streams.Verbose
            } else if (outputtype == 'Information') {var result = responce.Streams.Information
            } else if (outputtype == 'Debug') {var result = responce.Streams.Debug
            } else {}
            var result_string = JSON.stringify(result)
            $('#_result').val(result_string)
            if (!responce.Success || responce.Error || responce.Streams.HadErrors){
                $('#_result').removeClass('border-success _result_success').addClass('border-danger _result_error')
            } else {
                $('#_result').removeClass('border-danger _result_error').addClass('border-success _result_success')
            }
        },
        error: function(responce){
            $('#_result').removeClass('border-success _result_success').addClass('border-danger _result_error')
        }
    }

    if (method == 'POST') {
        var j_string = $('#_body').val()
        if (j_string) {
            request['data'] = j_string
        }
        request['contentType'] = 'application/json; charset=utf-8'
    }

    $.ajax(request);
} 

$( document ).ready(function() {
    update_url();
    $('#_wrapper').bind('click keyup', update_url);
    $('#_script').bind('click keyup', update_url);
    $('#Params').bind('click keyup', put_to_body);
    $('._btn_add_param').bind('click', add_param);
    $('._btn_del_param').bind('click', del_param);
    $('._btn_send').bind('click', send_body);

});


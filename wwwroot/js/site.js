// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

function load_scripts() {
    var request = {
        url: '/PowerShell/',
        method: 'GET',
        contentType: 'application/json; charset=utf-8',
        success: function(responce){
            console.log(responce)
            for (var i in responce.Wrappers) {$(`<option value="${responce.Wrappers[i]}">${responce.Wrappers[i]}</option>`).appendTo('#_wrapper')}
            for (var i in responce.Scripts) {$(`<option value="${responce.Scripts[i]}">${responce.Scripts[i]}</option>`).appendTo('#_script')}
            update_url()
        },
        error: function(responce){
        }
    }

    $.ajax(request);
}

function ani_send(d) {
    $({num: 0}).animate({num: 80}, {
        duration: d,
        easing: "swing",
        step: function(val) {
            int_val = Math.ceil(val)
            $('#_result').css('background',`linear-gradient(60deg, rgb(0,0,0), rgb(${int_val},${int_val},${int_val}), rgb(0,0,0))`);
        }
    });

    $({num: 80}).animate({num: 0}, {
        duration: d,
        easing: "swing",
        step: function(val) {
            int_val = Math.ceil(val)
            $('#_result').css('background',`linear-gradient(60deg, rgb(0,0,0), rgb(${int_val},${int_val},${int_val}), rgb(0,0,0))`);
        }
    });
}

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
    $('a#pwsh_url').prop('href',url)
    $('a#pwsh_url').html(url)
    console.log(url)
}

function send_body() {
    $('._btn_send').addClass('disabled')
    $('#_result').addClass('processing')
    $('#_result').removeClass('border-success _result_success border-danger _result_error')
    ani_send(200);
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
            $('._btn_send').removeClass('disabled')
            $('#_result').removeClass('processing')
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
            var result_string = JSON.stringify(result, null, 2)
            $('#_result ').val(result_string)
            if (!responce.Success || responce.Error || responce.Streams.HadErrors){
                $('#_result').removeClass('border-success _result_success').addClass('border-danger _result_error')
            } else {
                $('#_result').removeClass('border-danger _result_error').addClass('border-success _result_success')
            }
            ani_send(200);
        },
        error: function(responce){
            $('._btn_send').removeClass('disabled')
            $('#_result').removeClass('processing')
            $('#_result').removeClass('border-success _result_success').addClass('border-danger _result_error')
            ani_send(200);
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

function write_to_clip() {
    navigator.clipboard.writeText($('#_result').val())
}


$( document ).ready(function() {
    load_scripts();
    $('#_wrapper').bind('click keyup', update_url);
    $('#_script').bind('click keyup', update_url);
    $('#Params').bind('click keyup', put_to_body);
    $('._btn_add_param').bind('click', add_param);
    $('._btn_del_param').bind('click', del_param);
    $('._btn_send').bind('click', send_body);
    $('._btn_copy').bind('click', write_to_clip);
});

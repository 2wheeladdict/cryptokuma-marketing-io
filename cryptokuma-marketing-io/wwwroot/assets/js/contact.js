$(document).ready(function () {

    $("#submitContact").click(function (e) {

        //TODO if valid
        e.preventDefault();

        var name = $("#name").val(),
            email = $("#email").val(),
            message = $("#message").val();

        $.ajax({
            type: "POST",
            url: 'https://bb8f02s7yh.execute-api.us-east-1.amazonaws.com/v1',
            contentType: 'application/json',
            data: JSON.stringify({
                'firstname': firstname,
                'lastname': lastname,
                'email': email,
                'cookiestack': cookiestack
            }),
            success: function (res) {
                $('#form-response').text('Thanks!');
            },
            error: function () {
                $('#form-response').text('Error submitting contact information.');
            }
        });

    })

});